using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using CDReady;
class AiCheck {
    static int Main(string[] args) {
        try {
            var tracks=new List<Track> {
                new Track {OriginalName="01 - 测试歌手 - 示例曲目 [FLAC].flac",TagTitle="示例曲目",Artist="测试歌手",Title="测试歌手 - 示例曲目 [FLAC]"},
                new Track {OriginalName="02 - Singer - 99 Luftballons (Live).flac",TagTitle="99 Luftballons (Live)",Artist="Singer",Title="99 Luftballons (Live)"},
                new Track {OriginalName="03 - 歌手 - 原曲 (Remix).flac",TagTitle="原曲 (Remix)",Artist="歌手",Title="原曲 (Remix)"}
            };
            var result=LocalAi.Suggest(tracks,true,CancellationToken.None,Console.WriteLine).GetAwaiter().GetResult();
            if(result.Count!=3||result[0].After!="示例曲目"||result[1].After!="99 Luftballons (Live)"||result[2].After!="原曲 (Remix)")throw new Exception("Unexpected suggestions: "+Json.Encode(result));
            File.WriteAllText(args[0],Json.Encode(new {Success=true,Suggestions=result}),new UTF8Encoding(true));Console.WriteLine("PASS local LLM cleanup, version labels, and managed shutdown");return 0;
        }catch(Exception ex){File.WriteAllText(args[0],Json.Encode(new {Success=false,Error=ex.ToString()}),new UTF8Encoding(true));Console.WriteLine(ex);return 1;}
    }
}
