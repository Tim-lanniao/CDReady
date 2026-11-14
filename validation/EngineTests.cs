using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CDReady;

class EngineTests {
    static List<string> passed=new List<string>();
    static void Check(bool condition,string name){if(!condition)throw new Exception("FAILED: "+name);passed.Add(name);Console.WriteLine("PASS "+name);}
    static void Audio(Tools tools,string dir,string file,string input,string codec,string title,int channels) {
        ToolRunner.Run(tools.FFmpeg,new[]{"-nostdin","-v","error","-f","lavfi","-i",input,"-t","5","-ac",channels.ToString(),"-c:a",codec,"-metadata","title="+title,"-metadata","artist=测试歌手","-n",Path.Combine(dir,file)},CancellationToken.None).RequireSuccess("fixture");
    }
    static int Main(string[] args) {
        string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);string report=Path.Combine(root,"engine-tests.json");
        try {
            var token=CancellationToken.None;var tools=Tools.Find();
            var ai=LocalAiSettings.Validate(new LocalAiSettings {Endpoint="http://127.0.0.1:18081/v1",Model="test-model",Launcher=""});
            Check(ai.Endpoint=="http://127.0.0.1:18081/v1/","local AI endpoint normalized");
            bool remoteRejected=false;try{LocalAiSettings.Validate(new LocalAiSettings {Endpoint="https://example.com/v1",Model="test",Launcher=""});}catch(IOException){remoteRejected=true;}Check(remoteRejected,"local AI rejects remote endpoints");
            bool scriptRejected=false;try{LocalAiSettings.Validate(new LocalAiSettings {Endpoint="http://localhost:18081/v1",Model="test",Launcher="relative.ps1"});}catch(IOException){scriptRejected=true;}Check(scriptRejected,"local AI launcher requires an absolute script path");
            Check(Names.Clean("99 Luftballons (Live)")=="99 Luftballons (Live)","preserve numeric titles and version labels");
            Check(Names.FromFile("01 - 歌手 - 曲名 [FLAC].flac")=="曲名","clean track numbers, artist prefix and format markers");
            Check(Names.Safe("a:b/c?d") == "a b c d","safe output filenames");
            Check(Names.Output(2,"同名")=="02 - 同名.wav"&&Names.Output(3,"同名")=="03 - 同名.wav","duplicate titles remain unique by track number");
            var natural=new[]{"10.wav","2.wav","1.wav"}.OrderBy(x=>x,new NaturalComparer()).ToArray();Check(natural.SequenceEqual(new[]{"1.wav","2.wav","10.wav"}),"natural sorting 1,2,10");
            string inputs=Path.Combine(root,"inputs");Directory.CreateDirectory(inputs);
            Audio(tools,inputs,"10 - 歌手 - 测试原样 & $() `.flac","sine=frequency=997:sample_rate=44100","flac","99 Luftballons (Live)",2);
            Audio(tools,inputs,"02 - 高规格.flac","sine=frequency=17003:sample_rate=96000","flac","96k 曲目 (Remix)",2);
            // FFmpeg sine defaults to s16, so explicitly create a 24-bit FLAC at 96 kHz.
            string high=Path.Combine(inputs,"03 - 24bit.flac");ToolRunner.Run(tools.FFmpeg,new[]{"-nostdin","-v","error","-f","lavfi","-i","aevalsrc=1.07*sin(2*PI*997*t)|1.07*sin(2*PI*991*t):s=96000:d=5","-c:a","pcm_f32le","-metadata","title=浮点防削波","-n",Path.Combine(inputs,"04 - float.wav")},token).RequireSuccess("float fixture");
            ToolRunner.Run(tools.FFmpeg,new[]{"-nostdin","-v","error","-f","lavfi","-i","sine=frequency=999:sample_rate=96000:duration=5","-ac","2","-sample_fmt","s32","-c:a","flac","-metadata","title=24bit 转换","-n",high},token).RequireSuccess("24bit fixture");
            Audio(tools,inputs,"05 - mono.wav","sine=frequency=550:sample_rate=48000","pcm_s16le","单声道",1);
            Audio(tools,inputs,"06 - lossy.mp3","sine=frequency=600:sample_rate=48000","libmp3lame","有损来源",2);
            var tracks=Library.Scan(inputs,tools,token,null);Check(tracks.Count==6&&tracks.All(t=>t.Error==null),"probe six formats including unicode and shell metacharacters");
            var names=tracks.Select(t=>t.Title).ToArray();Check(names.Contains("99 Luftballons (Live)"),"numeric tag survives metadata import");
            Check(tracks.Single(t=>t.Source==high).Bits==24,"96 kHz 24-bit fixture detected correctly");
            string playlist=Path.Combine(inputs,"顺序.m3u8");File.WriteAllLines(playlist,new[]{"#EXTM3U",tracks.Last().OriginalName,tracks.First().OriginalName,"does-not-exist.wav"},new UTF8Encoding(true));
            var expectedFirst=tracks.Last().Source;int missing;int count=Playlists.Apply(tracks,playlist,out missing);Check(count==2&&missing==1&&tracks[0].Source==expectedFirst,"playlist order, missing entries, remaining files appended");
            var export=AudioEngine.Export(tracks,root,tools,false,token,null);Check(export.Tracks.Count==6,"batch export and strict decode verification");
            foreach(var row in export.Tracks)Check(AudioEngine.InspectWav(Path.Combine(export.Directory,row.File))==row.Frames,"canonical PCM WAV: "+row.File);
            var exact=export.Tracks.Single(t=>t.Title=="99 Luftballons (Live)");Check(exact.PcmUnchanged&&exact.SourcePcmMd5==exact.OutputPcmMd5,"44.1/16 stereo lossless PCM is byte-identical");
            Check(export.Tracks.Where(t=>t.Title=="24bit 转换"||t.Title=="96k 曲目 (Remix)").All(t=>t.Frames==220500),"96 kHz resampling preserves exact duration");
            Check(export.Tracks.Single(t=>t.Title=="浮点防削波").GainDb< -0.5,"automatic peak protection for above-full-scale float input");
            Check(export.Tracks.Single(t=>t.Title=="有损来源").LossySource,"lossy origin is disclosed");
            var exportedPlaylist=Playlists.Read(Path.Combine(export.Directory,"刻录顺序.wpl"));Check(exportedPlaylist.Select(x=>Path.GetFileName(x.Path)).SequenceEqual(export.Tracks.Select(x=>x.File)),"WPL order and unicode roundtrip");
            var renamed=tracks.Select(t=>new Track {Source=t.Source,OriginalName="different-"+t.OriginalName,Title=t.Title}).Reverse().ToList();count=Playlists.Apply(renamed,Path.Combine(export.Directory,"刻录顺序.wpl"),out missing);Check(count==6&&missing==0,"old converted playlist maps back to original titles");
            // Deliberately truncate a copy, never an original fixture.
            string broken=Path.Combine(root,"truncated.flac");byte[] bytes=File.ReadAllBytes(high);File.WriteAllBytes(broken,bytes.Take(bytes.Length/2).ToArray());var damaged=Library.Probe(broken,tools,token);bool failed=false;
            try{AudioEngine.Export(new[]{damaged},root,tools,false,token,null);}catch(IOException){failed=true;}Check(failed,"truncated FLAC rejected by complete PCM checksum");
            string rejected=Directory.GetDirectories(root,"_处理中*").Single();Check(File.Exists(Path.Combine(rejected,"未完成.txt"))&&!File.Exists(Path.Combine(rejected,"刻录顺序.wpl")),"failed batch never becomes ready or receives a playlist");
            var cts=new CancellationTokenSource();cts.Cancel();failed=false;try{AudioEngine.Export(tracks,root,tools,false,cts.Token,null);}catch(OperationCanceledException){failed=true;}Check(failed,"cancellation before processing");
            string cancelRoot=Path.Combine(root,"cancelled");var during=new CancellationTokenSource();failed=false;try{AudioEngine.Export(tracks,cancelRoot,tools,false,during.Token,p=>during.Cancel());}catch(IOException){failed=true;}
            Check(failed&&!Directory.GetDirectories(cancelRoot,"CDReady_*").Any()&&Directory.GetFiles(cancelRoot,"未完成.txt",SearchOption.AllDirectories).Length==1,"cancellation during processing never publishes a ready folder");
            failed=false;try{AudioEngine.Validate(new[]{new Track {Title="too long",Channels=2,Duration=4800}},false);}catch(IOException){failed=true;}Check(failed,"capacity limit includes gap allowance");
            failed=false;try{AudioEngine.Validate(new[]{new Track {Title="surround",Channels=6,Duration=5}},false);}catch(IOException){failed=true;}Check(failed,"multichannel requires explicit downmix opt-in");
            string badXml=Path.Combine(root,"unsafe.wpl");File.WriteAllText(badXml,"<!DOCTYPE smil [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><smil>&e;</smil>");failed=false;try{Playlists.Read(badXml);}catch(System.Xml.XmlException){failed=true;}Check(failed,"playlist external entities rejected");
            Check(export.Tracks.All(t=>AudioEngine.HashFile(t.Source,token)==t.SourceSha256),"all original files unchanged");
            File.WriteAllText(report,Json.Encode(new {Success=true,Checks=passed,Output=export.Directory}),new UTF8Encoding(true));return 0;
        } catch(Exception ex) {File.WriteAllText(report,Json.Encode(new {Success=false,Passed=passed,Error=ex.ToString()}),new UTF8Encoding(true));Console.WriteLine(ex);return 1;}
    }
}
