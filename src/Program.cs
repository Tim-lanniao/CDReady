using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CDReady {
    static class Program {
        [STAThread]
        static int Main(string[] args) {
            if(args.Length>0&&args[0]=="--prepare")return Prepare(args);
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException+=delegate(object s,System.Threading.ThreadExceptionEventArgs e){MessageBox.Show(e.Exception.Message,"CD Ready",MessageBoxButtons.OK,MessageBoxIcon.Error);};
            Application.Run(new MainForm(args.FirstOrDefault()));return 0;
        }
        static int Prepare(string[] args) {
            // The same processing engine is exposed for reproducible validation / local batch jobs.
            if(args.Length<4)return 2;
            string folder=args[1],output=args[2],log=args[3];
            try {
                var tools=Tools.Find();var tracks=Library.Scan(folder,tools,CancellationToken.None,null);
                string playlist=args.Length>4?args[4]:Playlists.Discover(folder).FirstOrDefault();int missing=0,matched=0;
                if(!string.IsNullOrEmpty(playlist))matched=Playlists.Apply(tracks,playlist,out missing);
                var result=AudioEngine.Export(tracks,output,tools,false,CancellationToken.None,x=>File.WriteAllText(log+".progress.txt",x.Text,Encoding.UTF8));
                File.WriteAllText(log,Json.Encode(new {Success=true,Output=result.Directory,Playlist=playlist,Matched=matched,Unmatched=missing,Tracks=result.Tracks}),new UTF8Encoding(true));return 0;
            }catch(Exception ex){File.WriteAllText(log,Json.Encode(new {Success=false,Error=ex.ToString()}),new UTF8Encoding(true));return 1;}
        }
    }
}
