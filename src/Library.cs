using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;

namespace CDReady {
    public static class Json {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 8388608 }; }
        public static string Encode(object value) { return Serializer().Serialize(value); }
        public static Dictionary<string, object> Parse(string text) { return Serializer().Deserialize<Dictionary<string, object>>(text); }
        public static string Str(IDictionary<string, object> d, string key, string fallback = "") {
            object v; return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : fallback;
        }
        public static int Int(IDictionary<string, object> d, string key) { int n; int.TryParse(Str(d,key), out n); return n; }
        public static double Number(IDictionary<string, object> d, string key) { double n; double.TryParse(Str(d,key),NumberStyles.Float,CultureInfo.InvariantCulture,out n); return n; }
        public static Dictionary<string,object> Obj(IDictionary<string,object> d,string key) { object v; return d != null && d.TryGetValue(key,out v) ? v as Dictionary<string,object> : null; }
        public static IEnumerable<object> Items(IDictionary<string,object> d,string key) { object v; return d != null && d.TryGetValue(key,out v) && v is IEnumerable ? ((IEnumerable)v).Cast<object>() : Enumerable.Empty<object>(); }
    }

    public class ToolResult {
        public int ExitCode; public string Output; public string Error;
        public void RequireSuccess(string context) { if (ExitCode != 0) throw new IOException(context + "\r\n" + Error); }
    }
    public static class ToolRunner {
        // Windows CRT quoting, without a shell. This also handles trailing backslashes and embedded quotes.
        public static string Quote(string value) {
            var b=new StringBuilder("\""); int slashes=0;
            foreach(char c in value) { if(c=='\\') { slashes++; continue; } if(c=='\"') b.Append('\\',slashes*2+1); else b.Append('\\',slashes); b.Append(c); slashes=0; }
            b.Append('\\',slashes*2); return b.Append('"').ToString();
        }
        static async Task<string> ReadTail(StreamReader reader) {
            var b=new StringBuilder(); var chars=new char[4096]; int n;
            while((n=await reader.ReadAsync(chars,0,chars.Length).ConfigureAwait(false))>0) { b.Append(chars,0,n); if(b.Length>131072) b.Remove(0,b.Length-65536); }
            return b.ToString();
        }
        public static ToolResult Run(string exe,IEnumerable<string> arguments,CancellationToken token,Stream binaryOutput=null,int timeoutSeconds=1800) {
            token.ThrowIfCancellationRequested();
            var si=new ProcessStartInfo(exe,string.Join(" ",arguments.Select(Quote))) { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8 };
            if(binaryOutput==null) si.StandardOutputEncoding=Encoding.UTF8;
            using(var p=new Process {StartInfo=si}) {
                p.Start(); var watch=Stopwatch.StartNew(); var error=ReadTail(p.StandardError);
                Task<string> text=binaryOutput==null ? ReadTail(p.StandardOutput) : null;
                Task copy=binaryOutput==null ? null : p.StandardOutput.BaseStream.CopyToAsync(binaryOutput);
                try {
                    while(!p.WaitForExit(150)) { token.ThrowIfCancellationRequested(); if(copy!=null && copy.IsFaulted) copy.GetAwaiter().GetResult(); if(watch.Elapsed.TotalSeconds>timeoutSeconds) throw new TimeoutException("音频工具运行超时。"); }
                    token.ThrowIfCancellationRequested();
                    if(copy!=null) copy.GetAwaiter().GetResult();
                    return new ToolResult {ExitCode=p.ExitCode,Output=text==null?"":text.GetAwaiter().GetResult(),Error=error.GetAwaiter().GetResult()};
                } finally { if(!p.HasExited) { try { p.Kill(); p.WaitForExit(5000); } catch {} } }
            }
        }
    }

    public class Tools {
        public string FFmpeg; public string FFprobe;
        public static Tools Find() {
            string root=AppDomain.CurrentDomain.BaseDirectory;
            var candidates=new List<string> {Path.Combine(root,"tools"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"scoop","apps","ffmpeg","current","bin")};
            string cfg=Path.Combine(root,"ffmpeg-path.txt"); if(File.Exists(cfg)) candidates.Insert(0,File.ReadAllText(cfg).Trim());
            candidates.AddRange((Environment.GetEnvironmentVariable("PATH")??"").Split(';'));
            foreach(string dir in candidates.Where(x=>!string.IsNullOrWhiteSpace(x))) {
                try { string a=Path.Combine(dir.Trim('"'),"ffmpeg.exe"),b=Path.Combine(dir.Trim('"'),"ffprobe.exe"); if(File.Exists(a)&&File.Exists(b)) return new Tools {FFmpeg=a,FFprobe=b}; } catch(ArgumentException) {}
            }
            throw new FileNotFoundException("未找到 FFmpeg 和 FFprobe。请点“设置工具”选择包含这两个 EXE 的文件夹（FFmpeg 需要支持 libsoxr）。");
        }
        public string Check(CancellationToken token) { var r=ToolRunner.Run(FFmpeg,new[]{"-hide_banner","-buildconf"},token); r.RequireSuccess("FFmpeg 不可用"); if(!(r.Output+r.Error).Contains("--enable-libsoxr")) throw new IOException("当前 FFmpeg 不含 libsoxr。请选择带 libsoxr 的 full 构建。"); return (ToolRunner.Run(FFmpeg,new[]{"-version"},token).Output.Split('\n').FirstOrDefault()??"").Trim(); }
    }

    public class Track {
        public string Source {get;set;} public string OriginalName {get;set;} public string TagTitle {get;set;} public string Artist {get;set;}
        public string Title {get;set;} public string Codec {get;set;} public string SampleFormat {get;set;}
        public int Rate {get;set;} public int Bits {get;set;} public int Channels {get;set;} public double Duration {get;set;}
        public int Disc {get;set;} public int TrackNumber {get;set;} public string Naming {get;set;} public string Error {get;set;}
        public bool Lossless {get;set;}
        public bool Exact {get {return Lossless && Rate==44100 && Bits==16 && Channels==2 && !Codec.StartsWith("pcm_f");}}
        public string FormatText {get {return Error!=null?"无法读取":string.Format(CultureInfo.InvariantCulture,"{0:0.###} kHz / {1} / {2}声道",Rate/1000.0,Bits>0?Bits+" bit":Codec,Channels);}}
        public string PlanText {get {return Error!=null?Error:Channels>2?"需开启多声道混音":Exact?"PCM 逐样本保持":(Lossless?"CD 规格转换":"有损来源 · CD 转换")+(Channels==1?" · 单声道复制":"");}}
    }

    public static class Names {
        public static string Key(string s) { return Regex.Replace((s??"").Normalize(NormalizationForm.FormKC),@"[^\p{L}\p{N}]","").ToLowerInvariant(); }
        public static string Clean(string text,bool stripNumber=false) {
            text=(text??"").Normalize(NormalizationForm.FormC).Trim();
            if(stripNumber)text=Regex.Replace(text,@"^\s*(?:(?:CD|DISC)\s*\d+\s*[-._ ]+)?\d{1,3}\s*(?:[-._、]\s*|\s+)","",RegexOptions.IgnoreCase);
            text=Regex.Replace(text,@"[\[【(（]\s*(?:FLAC|WAV|MP3|APE|Hi[- ]?Res|SQ|HQ|无损|超品|臻品|\d{2,3}\s*k(?:Hz|bps)|\d{1,2}\s*bit|\d{2,3}\s*[kK](?:Hz)?\s*[/_ -]\s*\d{1,2}\s*bit)\s*[\]】)）]","",RegexOptions.IgnoreCase);
            text=Regex.Replace(text,@"\.(?:flac|wav|mp3|m4a|aac|ape|aiff|aif|wma|ogg|opus|alac)$","",RegexOptions.IgnoreCase);
            return Regex.Replace(text,@"\s+"," ").Trim(' ','-','_');
        }
        public static string FromFile(string path) { string stem=Clean(Path.GetFileNameWithoutExtension(path),true); int p=stem.IndexOf(" - ",StringComparison.Ordinal); if(p>=0 && p+3<stem.Length) stem=stem.Substring(p+3); return Clean(stem); }
        public static string Safe(string title) {
            var invalid=Path.GetInvalidFileNameChars(); string s=new string(Clean(title).Select(c=>invalid.Contains(c)||char.IsControl(c)?' ':c).ToArray());
            s=Regex.Replace(s,@"\s+"," ").Trim(' ','.'); if(s.Length>90) s=s.Substring(0,90).TrimEnd(); if(s.Length==0) s="未命名";
            if(Regex.IsMatch(s,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase)) s="_"+s;
            return s;
        }
        public static string Output(int order,string title) {return order.ToString("D2",CultureInfo.InvariantCulture)+" - "+Safe(title)+".wav";}
    }

    public class NaturalComparer : IComparer<string> {
        public int Compare(string a,string b) {
            var aa=Regex.Matches(a??"",@"\d+|\D+"); var bb=Regex.Matches(b??"",@"\d+|\D+");
            for(int i=0;i<Math.Min(aa.Count,bb.Count);i++) {
                string x=aa[i].Value,y=bb[i].Value; int r;
                if(char.IsDigit(x[0])&&char.IsDigit(y[0])) { string nx=x.TrimStart('0'),ny=y.TrimStart('0'); r=nx.Length.CompareTo(ny.Length); if(r==0)r=string.CompareOrdinal(nx,ny); if(r==0)r=x.Length.CompareTo(y.Length); }
                else r=StringComparer.CurrentCultureIgnoreCase.Compare(x,y);
                if(r!=0)return r;
            }
            return aa.Count.CompareTo(bb.Count);
        }
    }

    public class PlaylistEntry { public string Path; public string Title; }
    public static class Playlists {
        static readonly string[] extensions={".wpl",".m3u8",".m3u",".pls"};
        public static List<string> Discover(string folder) {
            var all=Directory.GetFiles(folder).Where(IsPlaylist).OrderBy(Priority).ThenBy(x=>x,new NaturalComparer()).ToList();
            foreach(string child in Directory.GetDirectories(folder).OrderBy(x=>x,new NaturalComparer())) {
                if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0) continue;
                try {all.AddRange(Directory.GetFiles(child).Where(IsPlaylist).OrderBy(Priority).ThenBy(x=>x,new NaturalComparer()));} catch(UnauthorizedAccessException) {}
            }
            return all;
        }
        static bool IsPlaylist(string p){return extensions.Contains(Path.GetExtension(p).ToLowerInvariant());}
        static int Priority(string p){return Array.IndexOf(extensions,Path.GetExtension(p).ToLowerInvariant());}
        public static List<PlaylistEntry> Read(string path) {
            if(new FileInfo(path).Length>2097152) throw new IOException("播放列表过大（上限 2 MB）。");
            var result=new List<PlaylistEntry>(); string dir=Path.GetDirectoryName(Path.GetFullPath(path));
            if(Path.GetExtension(path).Equals(".wpl",StringComparison.OrdinalIgnoreCase)) {
                var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null }; var doc=new XmlDocument {XmlResolver=null};
                using(var reader=XmlReader.Create(path,settings))doc.Load(reader);
                foreach(XmlElement media in doc.SelectNodes("//*[local-name()='media']")) Add(result,dir,media.GetAttribute("src"),null);
            } else {
                string text; try { text=File.ReadAllText(path,new UTF8Encoding(false,true)); } catch(DecoderFallbackException) {text=File.ReadAllText(path,Encoding.Default);}
                string title=null;
                if(Path.GetExtension(path).Equals(".pls",StringComparison.OrdinalIgnoreCase)) {
                    foreach(var line in text.Split('\n').Where(x=>Regex.IsMatch(x,@"^File\d+=",RegexOptions.IgnoreCase)).OrderBy(x=>x,new NaturalComparer())) Add(result,dir,line.Substring(line.IndexOf('=')+1).Trim(),null);
                } else foreach(string raw in text.Split('\n')) {
                    string line=raw.Trim().TrimStart('\uFEFF');
                    if(line.StartsWith("#EXTINF:",StringComparison.OrdinalIgnoreCase)) {int i=line.IndexOf(',');title=i<0?null:line.Substring(i+1);}
                    else if(line.Length>0&&!line.StartsWith("#")){Add(result,dir,line,title);title=null;}
                }
            }
            return result;
        }
        static void Add(List<PlaylistEntry> items,string dir,string path,string title) {
            path=path.Trim().Trim('"'); Uri uri;
            if(Uri.TryCreate(path,UriKind.Absolute,out uri)) {if(!uri.IsFile)return; path=uri.LocalPath;}
            try {items.Add(new PlaylistEntry {Path=Path.GetFullPath(Path.IsPathRooted(path)?path:Path.Combine(dir,path)),Title=title});} catch(ArgumentException) {}
        }
        // Only reorders scanned files. A playlist never authorizes importing outside the selected folder.
        public static int Apply(List<Track> tracks,string path,out int unmatched) {
            var remaining=tracks.ToList(); var ordered=new List<Track>(); unmatched=0;
            foreach(var entry in Read(path)) {
                var found=remaining.Where(t=>string.Equals(t.Source,entry.Path,StringComparison.OrdinalIgnoreCase)).ToList();
                if(found.Count==0)found=remaining.Where(t=>string.Equals(t.OriginalName,Path.GetFileName(entry.Path),StringComparison.OrdinalIgnoreCase)).ToList();
                if(found.Count==0) {var key=Names.Key(Names.FromFile(entry.Path));found=remaining.Where(t=>Names.Key(t.Title)==key||Names.Key(Names.FromFile(t.Source))==key).ToList();}
                if(found.Count==1){ordered.Add(found[0]);remaining.Remove(found[0]);}else unmatched++;
            }
            int matched=ordered.Count; ordered.AddRange(remaining);tracks.Clear();tracks.AddRange(ordered);return matched;
        }
        public static void Write(string directory,IList<Track> tracks,IList<Conversion> conversions) {
            var doc=new XmlDocument();doc.AppendChild(doc.CreateProcessingInstruction("wpl","version=\"1.0\""));
            var smil=doc.CreateElement("smil");doc.AppendChild(smil);var head=doc.CreateElement("head");smil.AppendChild(head);var title=doc.CreateElement("title");title.InnerText="CD Ready";head.AppendChild(title);
            var body=doc.CreateElement("body");smil.AppendChild(body);var seq=doc.CreateElement("seq");body.AppendChild(seq);
            var m3u=new List<string>{"#EXTM3U"};
            for(int i=0;i<tracks.Count;i++) {string name=conversions[i].File;var media=doc.CreateElement("media");media.SetAttribute("src",name);seq.AppendChild(media);m3u.Add("#EXTINF:"+Math.Round(conversions[i].Duration).ToString(CultureInfo.InvariantCulture)+","+tracks[i].Title);m3u.Add(name);}
            doc.Save(Path.Combine(directory,"刻录顺序.wpl")); File.WriteAllLines(Path.Combine(directory,"刻录顺序.m3u8"),m3u,new UTF8Encoding(true));
        }
    }

    public static class Library {
        static readonly string[] extensions={".flac",".wav",".wave",".mp3",".m4a",".aac",".ape",".aif",".aiff",".wma",".ogg",".opus",".alac",".wv",".tta"};
        public static List<Track> Scan(string folder,Tools tools,CancellationToken token,Action<string> update) {
            if(!Directory.Exists(folder))throw new DirectoryNotFoundException("请选择存在的音频文件夹。");
            var files=Directory.GetFiles(folder).Where(p=>extensions.Contains(Path.GetExtension(p).ToLowerInvariant())).OrderBy(p=>Path.GetFileName(p),new NaturalComparer()).ToList();
            if(files.Count>2000)throw new IOException("一个任务最多载入 2000 首，请分文件夹处理。");
            var tracks=new List<Track>();
            for(int i=0;i<files.Count;i++) {token.ThrowIfCancellationRequested(); if(update!=null)update("读取 "+(i+1)+" / "+files.Count+" · "+Path.GetFileName(files[i]));
                try { tracks.Add(Probe(files[i],tools,token)); } catch(OperationCanceledException){throw;} catch(Exception ex) {tracks.Add(new Track { Source=files[i],OriginalName=Path.GetFileName(files[i]),Title=Names.FromFile(files[i]),Error=ex.Message,Codec="",Naming="需检查"});}
            }
            return tracks;
        }
        public static Track Probe(string path,Tools tools,CancellationToken token) {
            var r=ToolRunner.Run(tools.FFprobe,new[]{"-v","error","-select_streams","a:0","-show_entries","stream=codec_name,sample_fmt,sample_rate,channels,bits_per_sample,bits_per_raw_sample,duration:stream_tags=title,artist,track,disc:format=duration:format_tags=title,artist,track,disc","-of","json",path},token,null,90);r.RequireSuccess("无法读取文件信息");
            var root=Json.Parse(r.Output);var stream=Json.Items(root,"streams").OfType<Dictionary<string,object>>().FirstOrDefault();if(stream==null)throw new IOException("没有可读音轨；NCM 等加密文件需先用合法方式导出。");
            var format=Json.Obj(root,"format");var tags=Json.Obj(format,"tags")??new Dictionary<string,object>(); var st=Json.Obj(stream,"tags");if(st!=null)foreach(var kv in st)tags[kv.Key]=kv.Value;
            tags=new Dictionary<string,object>(tags,StringComparer.OrdinalIgnoreCase);
            var t=new Track {Source=Path.GetFullPath(path),OriginalName=Path.GetFileName(path),Codec=Json.Str(stream,"codec_name"),SampleFormat=Json.Str(stream,"sample_fmt"),Rate=Json.Int(stream,"sample_rate"),Channels=Json.Int(stream,"channels"),Bits=Json.Int(stream,"bits_per_raw_sample"),Duration=Json.Number(stream,"duration"),TagTitle=Json.Str(tags,"title"),Artist=Json.Str(tags,"artist")};
            if(t.Bits==0)t.Bits=Json.Int(stream,"bits_per_sample");if(t.Duration<=0)t.Duration=Json.Number(format,"duration");
            t.Lossless=t.Codec.StartsWith("pcm_",StringComparison.Ordinal)||new[]{"flac","alac","ape","wavpack","tta"}.Contains(t.Codec);
            if(t.Lossless&&t.Bits==0)t.Bits=t.SampleFormat.StartsWith("s16")?16:t.SampleFormat.StartsWith("s32")?32:0;
            int n;int.TryParse(Json.Str(tags,"track").Split('/')[0],out n);t.TrackNumber=n;int.TryParse(Json.Str(tags,"disc").Split('/')[0],out n);t.Disc=n;
            t.Title=string.IsNullOrWhiteSpace(t.TagTitle)?Names.FromFile(path):Names.Clean(t.TagTitle);
            // A title tag is authoritative; never split an intentional dash in it.
            t.Naming=string.IsNullOrWhiteSpace(t.TagTitle)?"文件名推断 · 可编辑":"曲名标签";
            if(string.IsNullOrWhiteSpace(t.Title)){t.Title=Names.FromFile(path);t.Naming="需核对";}
            if(t.Rate<=0||t.Channels<=0||t.Duration<=0)throw new IOException("采样率、声道或时长无效。");
            return t;
        }
    }
}
