using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CDReady {
    public class FlacInfo {
        public int Bits;public int Rate;public int Channels;public long Samples;public string Md5;
        public static FlacInfo Read(string path) {
            using(var s=File.OpenRead(path)) {var b=new byte[65536];int n=s.Read(b,0,b.Length);
                for(int p=0;p<n-42;p++) if(b[p]==102&&b[p+1]==76&&b[p+2]==97&&b[p+3]==67&&(b[p+4]&127)==0&&b[p+5]==0&&b[p+6]==0&&b[p+7]==34) {
                    int k=p+8;long frames=b[k+13]&15;for(int j=14;j<=17;j++)frames=(frames<<8)|b[k+j];
                    return new FlacInfo {Rate=(b[k+10]<<12)|(b[k+11]<<4)|(b[k+12]>>4),Channels=((b[k+12]&14)>>1)+1,Bits=(((b[k+12]&1)<<4)|(b[k+13]>>4))+1,Samples=frames,Md5=AudioEngine.Hex(b.Skip(k+18).Take(16).ToArray())};
                }
            }return null;
        }
    }
    public class Conversion {
        public int Order {get;set;}public string Title {get;set;}public string Source {get;set;}public string File {get;set;}
        public string SourceCodec {get;set;}public int SourceRate {get;set;}public int SourceBits {get;set;}public int SourceChannels {get;set;}
        public bool LossySource {get;set;}public bool PcmUnchanged {get;set;}public string SourceIntegrity {get;set;}public string SourceSha256 {get;set;}
        public string SourcePcmMd5 {get;set;}public string OutputPcmMd5 {get;set;}public string OutputSha256 {get;set;}
        public long Frames {get;set;}public double Duration {get;set;}public double GainDb {get;set;}public string Filter {get;set;}public string Note {get;set;}
    }
    public class ExportResult {public string Directory;public List<Conversion> Tracks;}
    public class ProgressInfo {public int Done;public int Total;public string Text;}
    public static class AudioEngine {
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        public static string Hex(byte[] b){return BitConverter.ToString(b).Replace("-","").ToLowerInvariant();}
        public static string HashFile(string path,CancellationToken token,long offset=0,bool md5=false) {
            using(var hash=md5?(HashAlgorithm)MD5.Create():SHA256.Create()) using(var s=File.OpenRead(path)) {
                s.Position=offset;var buf=new byte[131072];int n;while((n=s.Read(buf,0,buf.Length))>0) {token.ThrowIfCancellationRequested();hash.TransformBlock(buf,0,n,null,0);}hash.TransformFinalBlock(new byte[0],0,0);return Hex(hash.Hash);
            }
        }
        public static double CdSeconds(IEnumerable<Track> tracks){return tracks.Sum(t=>Math.Ceiling(t.Duration*75)/75+2);}
        public static void Validate(IList<Track> tracks,bool downmix) {
            if(tracks.Count==0)throw new IOException("请先载入音频。");
            if(tracks.Count>99)throw new IOException("一张音频 CD 最多 99 轨，请移出部分曲目后分批准备。");
            foreach(var t in tracks) {if(t.Error!=null)throw new IOException(t.OriginalName+"："+t.Error);if(t.Channels>2&&!downmix)throw new IOException(t.Title+"：包含多声道。请先开启“允许多声道混音”或移出该曲目。");if(t.Duration<4)throw new IOException(t.Title+"：少于 4 秒，不满足本工具的 Audio CD 轨道要求。");}
            if(CdSeconds(tracks)>4800)throw new IOException("超过 80 分钟光盘容量（含每轨 2 秒间隔预留）。请分批处理。");
            if(tracks.Select(t=>t.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=tracks.Count)throw new IOException("列表含重复文件。");
        }
        static List<string> Input(string path,bool strict){var a=new List<string>{"-nostdin","-hide_banner","-v","error"};if(strict)a.AddRange(new[]{"-xerror","-err_detect","explode"});a.AddRange(new[]{"-i",path,"-map","0:a:0","-vn","-sn","-dn"});return a;}
        static string HashFrom(ToolResult r){var m=Regex.Match(r.Output,@"MD5=([a-fA-F0-9]{32})");return m.Success?m.Groups[1].Value.ToLowerInvariant():"";}
        static string VerifySource(Track t,Tools tools,CancellationToken token,out string integrity) {
            FlacInfo info=t.Codec=="flac"?FlacInfo.Read(t.Source):null;
            if(info!=null&&new[]{8,16,24,32}.Contains(info.Bits)&&info.Md5!=new string('0',32)&&info.Samples>0) {
                var a=Input(t.Source,false);a.AddRange(new[]{"-c:a",info.Bits==8?"pcm_s8":"pcm_s"+info.Bits+"le","-f","hash","-hash","md5","-"});var r=ToolRunner.Run(tools.FFmpeg,a,token);string actual=HashFrom(r);
                if(actual!=info.Md5)throw new IOException("FLAC 的完整 PCM 校验不通过，可能截断或损坏："+t.Title);
                if(info.Rate!=t.Rate||info.Bits!=t.Bits||info.Channels!=t.Channels)throw new IOException("FLAC 标头与解码信息不一致。");
                t.Duration=(double)info.Samples/info.Rate; integrity="完整 PCM 与 FLAC 内置 MD5 一致";
                if(r.ExitCode!=0||!string.IsNullOrWhiteSpace(r.Error))integrity+="；存在非音频尾部/封装警告，完整 PCM 已验证";
                return actual;
            }
            var args=Input(t.Source,true);args.AddRange(new[]{"-c:a",t.Exact?"pcm_s16le":"pcm_f64le","-f","hash","-hash","md5","-"});var decoded=ToolRunner.Run(tools.FFmpeg,args,token);decoded.RequireSuccess("源文件完整解码失败："+t.Title);string hash=HashFrom(decoded);if(hash.Length==0)throw new IOException("源文件没有输出可校验的 PCM。");
            integrity="完整严格解码通过（源格式无可用的内置 PCM 校验）";return hash;
        }
        static string InitialFilter(Track t) {
            // Mono duplication preserves the signal amplitude in both channels.
            string channels=t.Channels==1?"pan=stereo|c0=c0|c1=c0,":t.Channels>2?"aformat=sample_fmts=dbl:channel_layouts=stereo,":"";
            return channels+"aresample=44100:resampler=soxr:precision=28:osf=dbl";
        }
        static void WriteHeader(Stream s,long frames) {
            long bytes=frames*4;if(bytes>uint.MaxValue-36)throw new IOException("WAV 超过 RIFF 限制。");s.Position=0;
            using(var w=new BinaryWriter(s,Encoding.ASCII,true)) {w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write((uint)(bytes+36));w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((ushort)1);w.Write((ushort)2);w.Write(44100);w.Write(176400);w.Write((ushort)4);w.Write((ushort)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write((uint)bytes);}
        }
        public static long InspectWav(string path) {
            using(var s=File.OpenRead(path))using(var r=new BinaryReader(s)) {
                if(s.Length<44||Encoding.ASCII.GetString(r.ReadBytes(4))!="RIFF"||r.ReadUInt32()!=s.Length-8||Encoding.ASCII.GetString(r.ReadBytes(8))!="WAVEfmt "||r.ReadInt32()!=16||r.ReadUInt16()!=1||r.ReadUInt16()!=2||r.ReadInt32()!=44100||r.ReadInt32()!=176400||r.ReadUInt16()!=4||r.ReadUInt16()!=16||Encoding.ASCII.GetString(r.ReadBytes(4))!="data"||r.ReadUInt32()!=s.Length-44||(s.Length-44)%4!=0)throw new IOException("输出 WAV 标头不符合标准 44.1 kHz / 16-bit / 双声道 PCM。");
                return(s.Length-44)/4;
            }
        }
        static Conversion ConvertOne(Track t,int order,string dest,Tools tools,CancellationToken token,Action<string> update) {
            // Holding a read lease disallows source writes/deletes for the entire conversion.
            using(var lease=new FileStream(t.Source,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                update("检查原文件完整性 · "+t.Title);string sourceHash=HashFile(t.Source,token),integrity;string sourcePcm=VerifySource(t,tools,token,out integrity);
                string filter="";double gain=0;long expected=0;
                if(!t.Exact) {
                    update("分析转换后峰值 · "+t.Title);filter=InitialFilter(t);
                    var pa=Input(t.Source,false);pa[3]="info";pa.AddRange(new[]{"-af",filter+",astats=metadata=0:reset=0","-f","null","-"});var peak=ToolRunner.Run(tools.FFmpeg,pa,token);
                    // A FLAC whose built-in checksum passed may carry non-audio tail data.
                    if(peak.ExitCode!=0&&!integrity.StartsWith("完整 PCM 与 FLAC"))peak.RequireSuccess("峰值分析失败");
                    var matches=Regex.Matches(peak.Error,@"Peak level dB:\s*(-?\d+(?:\.\d+)?|-inf)");if(matches.Count==0)throw new IOException("未能完成峰值分析："+t.Title);
                    double max=double.NegativeInfinity;foreach(Match m in matches)if(m.Groups[1].Value!="-inf")max=Math.Max(max,double.Parse(m.Groups[1].Value,Inv));
                    if(max> -0.01)gain=-Math.Ceiling((max+0.01)*100)/100;
                    var counts=Regex.Matches(peak.Error,@"Number of samples:\s*(\d+)");if(counts.Count==0)throw new IOException("无法验证转换采样数。");expected=long.Parse(counts[counts.Count-1].Groups[1].Value,Inv);
                    if(gain!=0)filter+=",volume="+gain.ToString("0.00",Inv)+"dB:precision=double";
                    filter+=",aresample=osf=s16:dither_method=triangular";
                }
                update((t.Exact?"保持 PCM 并写入 WAV · ":"转换 CD PCM 并写入 WAV · ")+t.Title);
                var a=Input(t.Source,false);if(filter.Length>0)a.AddRange(new[]{"-af",filter});a.AddRange(new[]{"-map_metadata","-1","-c:a","pcm_s16le","-f","s16le","-"});
                long frames;
                using(var output=new FileStream(dest,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)) {
                    output.Write(new byte[44],0,44);var result=ToolRunner.Run(tools.FFmpeg,a,token,output);
                    if(result.ExitCode!=0&&!integrity.StartsWith("完整 PCM 与 FLAC"))result.RequireSuccess("音频转换失败");
                    if((output.Length-44)%4!=0)throw new IOException("PCM 数据长度错误。");frames=(output.Length-44)/4;
                    if(frames<=0||(!t.Exact&&frames!=expected))throw new IOException("转换后的采样数不一致。");
                    // Lossless inputs have a reliable native duration; verify no truncation even in exact mode.
                    if(t.Lossless&&Math.Abs(frames-t.Duration*44100)>2)throw new IOException("转换时长与原文件不一致："+t.Title);
                    WriteHeader(output,frames);output.Flush(true);
                }
                update("校验 WAV 和 PCM · "+t.Title);if(InspectWav(dest)!=frames)throw new IOException("WAV 长度校验失败。");
                string outputPcm=HashFile(dest,token,44,true);if(t.Exact&&sourcePcm!=outputPcm)throw new IOException("无转换曲目的 PCM 校验不一致。");
                var verify=Input(dest,true);verify.AddRange(new[]{"-f","null","-"});ToolRunner.Run(tools.FFmpeg,verify,token).RequireSuccess("输出文件严格解码失败");
                if(HashFile(t.Source,token)!=sourceHash)throw new IOException("处理过程中原文件发生了变化。");
                return new Conversion {Order=order,Title=t.Title,Source=t.Source,File=Path.GetFileName(dest),SourceCodec=t.Codec,SourceRate=t.Rate,SourceBits=t.Bits,SourceChannels=t.Channels,LossySource=!t.Lossless,PcmUnchanged=t.Exact,SourceIntegrity=integrity,SourceSha256=sourceHash,SourcePcmMd5=sourcePcm,OutputPcmMd5=outputPcm,OutputSha256=HashFile(dest,token),Frames=frames,Duration=frames/44100.0,GainDb=gain,Filter=filter,Note=t.Exact?"解码后的 16-bit PCM 完全一致；只更换封装":"转换到 CD 规格，非逐位无损；未进行响度归一化"};
            }
        }
        public static ExportResult Export(IList<Track> tracks,string parent,Tools tools,bool downmix,CancellationToken token,Action<ProgressInfo> update) {
            Validate(tracks,downmix);parent=Path.GetFullPath(parent);Directory.CreateDirectory(parent);string version=tools.Check(token);
            var drive=new DriveInfo(Path.GetPathRoot(parent));long required=(long)(tracks.Sum(t=>t.Duration)*176400*1.1)+104857600;if(drive.IsReady&&drive.AvailableFreeSpace<required)throw new IOException("输出磁盘剩余空间不足。");
            string suffix=DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,5);
            string stage=Path.Combine(parent,"_处理中_CDReady_"+suffix),final=Path.Combine(parent,"CDReady_"+suffix);
            Directory.CreateDirectory(stage);File.WriteAllText(Path.Combine(stage,"未完成.txt"),"处理尚未完成。请勿刻录此文件夹。",Encoding.UTF8);
            var rows=new List<Conversion>();
            try {
                for(int i=0;i<tracks.Count;i++) {token.ThrowIfCancellationRequested();int k=i;string dest=Path.Combine(stage,Names.Output(i+1,tracks[i].Title));
                    rows.Add(ConvertOne(tracks[i],i+1,dest,tools,token,delegate(string s){if(update!=null)update(new ProgressInfo {Done=k,Total=tracks.Count,Text=s});}));
                    if(update!=null)update(new ProgressInfo {Done=i+1,Total=tracks.Count,Text="已校验 "+(i+1)+" / "+tracks.Count});
                }
                if(rows.Sum(t=>Math.Ceiling(t.Frames/588.0)/75+2)>4800)throw new IOException("实际输出超过 80 分钟 CD 容量。");
                Playlists.Write(stage,tracks,rows);File.WriteAllText(Path.Combine(stage,"cdready-manifest.json"),Json.Encode(new {Schema=1,App="CD Ready 1.0",Status="verified",Created=DateTime.Now.ToString("o"),FFmpeg=version,OutputRate=44100,OutputBits=16,OutputChannels=2,Tracks=rows}),new UTF8Encoding(true));
                var report=new StringBuilder("CD Ready · 刻录准备报告\r\n\r\n全部文件已通过检查：44.1 kHz / 16-bit / 双声道 PCM WAV。\r\n原始音频未修改。新文件按列表编号、清理曲名。\r\n\r\n使用方法：\r\n1. 打开 刻录顺序.wpl，把该列表添加到旧版 Windows Media Player 的刻录栏。\r\n2. 选择“音频 CD”，确认按 01、02…排列；不要混入原文件。\r\n3. 开始刻录。若 WMP 仍提示个别文件错误，可先播放该 WAV 再停止后重试。\r\n本软件验证的是文件，光盘和刻录机状态仍需由刻录软件检查。\r\n\r\n音质说明：\r\n44.1 kHz / 16-bit / 双声道无损来源保持解码 PCM 完全一致。\r\n其他规格通过 SoX 高精度重采样和三角抖动量化为 CD 规格；不属于逐位无损。\r\n仅在转换后峰值将超过预留范围时，做所需的最小整轨衰减；不做响度归一化。\r\n有损音源不会因为转成 WAV 而恢复丢失信息。\r\n单声道复制到左右声道；多声道仅在用户开启混音后转换。\r\n\r\n");
                report.AppendFormat("总音频时长：{0:0.000} 秒；含每轨 2 秒间隔预留：{1:0.00} 分钟。\r\n\r\n",rows.Sum(x=>x.Duration),rows.Sum(x=>Math.Ceiling(x.Frames/588.0)/75+2)/60);
                foreach(var row in rows) {report.AppendFormat("{0}\r\n  来源：{1} Hz / {2} bit / {3} 声道 / {4}\r\n  {5}；防削波增益 {6:0.00} dB\r\n  {7}\r\n",row.File,row.SourceRate,row.SourceBits,row.SourceChannels,row.SourceCodec,row.Note,row.GainDb,row.SourceIntegrity);}
                report.Append("\r\nFFmpeg 处理参数与哈希记录见 cdready-manifest.json。\r\n");File.WriteAllText(Path.Combine(stage,"刻录说明与校验报告.txt"),report.ToString(),new UTF8Encoding(true));
                token.ThrowIfCancellationRequested();File.Delete(Path.Combine(stage,"未完成.txt"));Directory.Move(stage,final);return new ExportResult {Directory=final,Tracks=rows};
            } catch(Exception ex) {File.WriteAllText(Path.Combine(stage,"未完成.txt"),"此任务未完成，请勿刻录此文件夹。\r\n"+ex.Message+"\r\n已完成的校验记录：\r\n"+Json.Encode(rows),new UTF8Encoding(true));throw new IOException((ex is OperationCanceledException?"已取消。":"准备失败："+ex.Message)+"\r\n原文件未修改。未完成文件保留在：\r\n"+stage,ex);}
        }
    }
}
