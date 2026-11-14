using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CDReady {
    public class MainForm : Form {
        readonly Color ink=Color.FromArgb(28,43,51),muted=Color.FromArgb(92,111,123),accent=Color.FromArgb(0,116,105);
        TextBox sourceBox,outputBox;ComboBox orderBox,playlistBox;DataGridView grid;Label summary,status,playlistNote;ProgressBar progress;
        Button exportButton,cancelButton,openButton,playlistButton;CheckBox downmixBox,aiStart;Panel controls;
        List<Track> tracks=new List<Track>();List<string> playlists=new List<string>();Tools tools;CancellationTokenSource cancel;string outputFolder;bool busy,loading;
        Point dragOrigin,dragPoint;int dragSource=-1,dropIndex=-1,dragSelection=-1;bool dragging;
        readonly string dragFormat="CDReady.Track."+Guid.NewGuid().ToString("N");
        System.Windows.Forms.Timer dragScrollTimer;
        readonly string initialFolder;
        public MainForm(string folder) {
            SuspendLayout();
            initialFolder=folder;Text="CD Ready · 音频刻录准备";Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(244,247,248);ForeColor=ink;
            AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);ClientSize=new Size(1190,810);MinimumSize=new Size(1060,730);StartPosition=FormStartPosition.CenterScreen;
            Build();Shown+=async delegate {ScaleGrid();if(!string.IsNullOrEmpty(initialFolder)&&Directory.Exists(initialFolder)){sourceBox.Text=initialFolder;await LoadFolder();}};
            DpiChanged+=delegate {BeginInvoke(new Action(ScaleGrid));};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;MessageBox.Show(this,"正在处理。请先点“取消”，等当前操作停止后再关闭。","CD Ready");}};
            ResumeLayout(true);
        }
        Button Btn(string text,EventHandler action,bool primary=false,int width=112) {
            var b=new Button {Text=text,AutoSize=false,Size=new Size(width,34),FlatStyle=FlatStyle.Flat,BackColor=primary?accent:Color.White,ForeColor=primary?Color.White:ink,Cursor=Cursors.Hand,Margin=new Padding(0,0,8,0)};
            b.FlatAppearance.BorderColor=primary?accent:Color.FromArgb(207,217,222);b.FlatAppearance.BorderSize=1;b.Click+=action;return b;
        }
        Label LabelText(string text,int size=10,bool bold=false) {return new Label {Text=text,AutoSize=true,ForeColor=muted,Font=new Font(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular),Margin=new Padding(0,0,0,8)};}
        FlowLayoutPanel Row(){return new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Margin=new Padding(0),Padding=new Padding(0,2,0,2)};}
        void Build() {
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(26,20,26,18),ColumnCount=1,RowCount=8};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,188));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));Controls.Add(layout);
            var header=new Panel {Dock=DockStyle.Fill};header.Controls.Add(new Label {Text="CD Ready",Location=new Point(0,0),AutoSize=true,Font=new Font(Font.FontFamily,25,FontStyle.Bold),ForeColor=ink});
            header.Controls.Add(new Label {Text="整理曲序  /  清理曲名  /  准备音频 CD",Location=new Point(3,48),AutoSize=true,ForeColor=muted});
            layout.Controls.Add(header,0,0);
            controls=new Panel {Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(12)};layout.Controls.Add(controls,0,1);
            var options=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=4};options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,86));options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));for(int i=0;i<4;i++)options.RowStyles.Add(new RowStyle(SizeType.Percent,25));controls.Controls.Add(options);
            options.Controls.Add(LabelText("音频文件夹"),0,0);sourceBox=new TextBox {Dock=DockStyle.Fill,AccessibleName="音频文件夹"};options.Controls.Add(sourceBox,1,0);options.Controls.Add(Btn("选择并载入",async delegate {using(var d=new FolderBrowserDialog {Description="选择音频所在文件夹（只读取本层音频）",SelectedPath=sourceBox.Text,ShowNewFolderButton=false})if(d.ShowDialog(this)==DialogResult.OK){sourceBox.Text=d.SelectedPath;await LoadFolder();}},false,130),2,0);
            sourceBox.KeyDown+=async delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await LoadFolder();}};
            options.Controls.Add(LabelText("输出到"),0,1);outputBox=new TextBox {Dock=DockStyle.Fill,AccessibleName="输出位置"};options.Controls.Add(outputBox,1,1);options.Controls.Add(Btn("选择位置",delegate{using(var d=new FolderBrowserDialog {Description="选择输出位置；软件会自动新建文件夹",SelectedPath=outputBox.Text})if(d.ShowDialog(this)==DialogResult.OK)outputBox.Text=d.SelectedPath;},false,130),2,1);
            options.Controls.Add(LabelText("排序方式"),0,2);orderBox=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Left,Width=345};orderBox.Items.AddRange(new object[]{"优先沿用播放列表顺序","文件名自然排序","标签：碟号 / 音轨号","手动顺序"});orderBox.SelectedIndex=0;orderBox.SelectedIndexChanged+=delegate{if(!loading)SortTracks();};options.Controls.Add(orderBox,1,2);options.Controls.Add(Btn("重新读取",async delegate{await LoadFolder();},false,130),2,2);
            options.Controls.Add(LabelText("播放列表"),0,3);playlistBox=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,AccessibleName="播放列表"};playlistBox.SelectedIndexChanged+=delegate{if(!loading&&orderBox.SelectedIndex==0)SortTracks();};options.Controls.Add(playlistBox,1,3);options.Controls.Add(Btn("指定列表…",delegate{using(var d=new OpenFileDialog {Filter="播放列表|*.wpl;*.m3u8;*.m3u;*.pls",InitialDirectory=sourceBox.Text})if(d.ShowDialog(this)==DialogResult.OK){if(!playlists.Contains(d.FileName)){playlists.Add(d.FileName);playlistBox.Items.Add(d.FileName);}playlistBox.SelectedIndex=playlists.IndexOf(d.FileName);orderBox.SelectedIndex=0;SortTracks();}},false,130),2,3);
            var toolbar=Row();layout.Controls.Add(toolbar,0,2);
            toolbar.Controls.Add(Btn("↑ 上移",delegate{MoveTrack(-1);},false,80));toolbar.Controls.Add(Btn("↓ 下移",delegate{MoveTrack(1);},false,80));toolbar.Controls.Add(Btn("移出列表",delegate{RemoveTrack();},false,96));toolbar.Controls.Add(Btn("重新清理曲名",delegate{foreach(var t in tracks){t.Title=string.IsNullOrWhiteSpace(t.TagTitle)?Names.FromFile(t.Source):Names.Clean(t.TagTitle);t.Naming=string.IsNullOrWhiteSpace(t.TagTitle)?"文件名推断 · 可编辑":"曲名标签";}RefreshGrid();},false,130));toolbar.Controls.Add(Btn("本地 AI 校名",async delegate{await AiNames();},false,126));
            playlistNote=LabelText("按住曲目行上下拖动；双击曲名可编辑。");playlistNote.Margin=new Padding(10,8,0,0);toolbar.Controls.Add(playlistNote);
            grid=new DataGridView {Dock=DockStyle.Fill,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,RowHeadersWidth=30,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,AutoGenerateColumns=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=true,EnableHeadersVisualStyles=false,EditMode=DataGridViewEditMode.EditOnKeystrokeOrF2,AllowDrop=true};
            grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle {BackColor=Color.FromArgb(230,237,239),ForeColor=ink,Font=new Font(Font,FontStyle.Bold),SelectionBackColor=Color.FromArgb(230,237,239)};grid.ColumnHeadersHeight=36;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.DefaultCellStyle=new DataGridViewCellStyle {BackColor=Color.White,ForeColor=ink,SelectionBackColor=Color.FromArgb(215,239,233),SelectionForeColor=ink,Padding=new Padding(3)};grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(248,250,251);grid.RowTemplate.Height=34;grid.GridColor=Color.FromArgb(230,235,237);
            AddColumn("number","#",46,true);AddColumn("title","输出曲名（双击修改）",240,false);AddColumn("duration","时长",68,true);AddColumn("format","源格式",205,true);AddColumn("plan","处理方式",180,true);AddColumn("name","命名依据",150,true);AddColumn("output","输出文件预览",240,true);AddColumn("source","原文件",300,true);
            grid.Columns["name"].Visible=false;grid.Columns["source"].Visible=false;grid.Columns["output"].DisplayIndex=2;
            grid.Columns["title"].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;grid.Columns["title"].MinimumWidth=180;
            grid.CellEndEdit+=delegate(object s,DataGridViewCellEventArgs e){if(e.RowIndex>=0&&e.ColumnIndex==1){var t=tracks[e.RowIndex];string text=Convert.ToString(grid.Rows[e.RowIndex].Cells[1].Value);if(!string.IsNullOrWhiteSpace(text)){t.Title=Names.Clean(text);t.Naming="手动修改";}grid.Rows[e.RowIndex].Cells[1].Value=t.Title;grid.Rows[e.RowIndex].Cells[5].Value=t.Naming;grid.Rows[e.RowIndex].Cells[6].Value=Names.Output(e.RowIndex+1,t.Title);}};
            grid.CellDoubleClick+=delegate(object s,DataGridViewCellEventArgs e){if(e.RowIndex>=0&&e.ColumnIndex==1)grid.BeginEdit(true);};
            grid.KeyDown+=delegate(object s,KeyEventArgs e){if(e.Alt&&e.KeyCode==Keys.Up){MoveTrack(-1);e.Handled=true;}if(e.Alt&&e.KeyCode==Keys.Down){MoveTrack(1);e.Handled=true;}};
            WireRowDragging();
            layout.Controls.Add(grid,0,3);
            var totals=Row();summary=LabelText("选择一个文件夹开始",11,true);summary.ForeColor=accent;summary.Margin=new Padding(0,11,0,0);totals.Controls.Add(summary);layout.Controls.Add(totals,0,4);
            var settings=Row();downmixBox=new CheckBox {Text="允许多声道混音为立体声",AutoSize=true,Margin=new Padding(0,7,18,0)};settings.Controls.Add(downmixBox);aiStart=new CheckBox {Text="按需启停本地模型",AutoSize=true,Checked=true,Margin=new Padding(0,7,18,0)};settings.Controls.Add(aiStart);settings.Controls.Add(Btn("设置工具",delegate{using(var d=new FolderBrowserDialog {Description="选择同时包含 ffmpeg.exe 和 ffprobe.exe 的 bin 文件夹"})if(d.ShowDialog(this)==DialogResult.OK){if(!File.Exists(Path.Combine(d.SelectedPath,"ffmpeg.exe"))||!File.Exists(Path.Combine(d.SelectedPath,"ffprobe.exe"))){MessageBox.Show(this,"这个文件夹缺少 ffmpeg.exe 或 ffprobe.exe。");return;}try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ffmpeg-path.txt"),d.SelectedPath);tools=Tools.Find();status.Text="已设置音频工具。";}catch(Exception ex){ShowError(ex);}}},false,96));
            settings.Controls.Add(Btn("使用说明",delegate{string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"使用说明.txt");if(File.Exists(p))Process.Start(new ProcessStartInfo(p){UseShellExecute=true});},false,96));layout.Controls.Add(settings,0,5);
            var actions=Row();exportButton=Btn("生成刻录文件",async delegate{await Export();},true,170);actions.Controls.Add(exportButton);cancelButton=Btn("取消",delegate{if(cancel!=null){cancel.Cancel();cancelButton.Enabled=false;status.Text="正在安全停止，请稍候…";}},false,80);cancelButton.Enabled=false;actions.Controls.Add(cancelButton);openButton=Btn("打开输出文件夹",delegate{OpenOutput(false);},false,150);openButton.Enabled=false;actions.Controls.Add(openButton);playlistButton=Btn("打开 WMP 列表",delegate{OpenOutput(true);},false,150);playlistButton.Enabled=false;actions.Controls.Add(playlistButton);layout.Controls.Add(actions,0,6);
            var bottom=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=new Padding(0)};bottom.RowStyles.Add(new RowStyle(SizeType.Absolute,8));bottom.RowStyles.Add(new RowStyle(SizeType.Absolute,25));bottom.RowStyles.Add(new RowStyle(SizeType.Absolute,23));progress=new ProgressBar {Dock=DockStyle.Fill,Margin=new Padding(0)};status=LabelText("原文件保留；所有输出写入新文件夹。",9);status.Dock=DockStyle.Fill;status.AutoEllipsis=true;
            bottom.Controls.Add(progress,0,0);bottom.Controls.Add(status,0,1);var note=LabelText("高规格音源转 CD 需重采样 / 量化；44.1 kHz · 16-bit · 双声道无损来源保持 PCM 完全一致。",9);note.Dock=DockStyle.Fill;bottom.Controls.Add(note,0,2);layout.Controls.Add(bottom,0,7);
        }
        void AddColumn(string name,string header,int width,bool readOnly){grid.Columns.Add(new DataGridViewTextBoxColumn {Name=name,HeaderText=header,Width=width,ReadOnly=readOnly,SortMode=DataGridViewColumnSortMode.NotSortable});}
        void WireRowDragging() {
            dragScrollTimer=new System.Windows.Forms.Timer {Interval=90};
            dragScrollTimer.Tick+=delegate {
                if(!dragging||busy||tracks.Count==0)return;
                Point p=grid.PointToClient(Control.MousePosition);if(!grid.ClientRectangle.Contains(p))return;
                int margin=Math.Max(20,grid.RowTemplate.Height/2),first=grid.FirstDisplayedScrollingRowIndex;
                int direction=p.Y<grid.ColumnHeadersHeight+margin?-1:p.Y>grid.ClientSize.Height-margin?1:0;
                if(direction!=0&&first>=0) {
                    int next=Math.Max(0,Math.Min(tracks.Count-1,first+direction));
                    if(next!=first)try {grid.FirstDisplayedScrollingRowIndex=next;}catch(InvalidOperationException){}
                    dragPoint=p;dropIndex=DropPosition(p);grid.Invalidate();
                }
            };
            FormClosed+=delegate{dragScrollTimer.Dispose();};
            grid.MouseDown+=delegate(object s,MouseEventArgs e) {
                dragSource=-1;if(busy||e.Button!=MouseButtons.Left||e.Clicks!=1||grid.IsCurrentCellInEditMode)return;
                var hit=grid.HitTest(e.X,e.Y);if(hit.RowIndex<0)return;
                dragSource=hit.RowIndex;dragOrigin=e.Location;
            };
            grid.MouseUp+=delegate{if(!dragging)dragSource=-1;};
            grid.MouseMove+=delegate(object s,MouseEventArgs e) {
                if(busy||dragging||dragSource<0||e.Button!=MouseButtons.Left)return;
                var size=SystemInformation.DragSize;var threshold=new Rectangle(dragOrigin.X-size.Width/2,dragOrigin.Y-size.Height/2,size.Width,size.Height);
                if(threshold.Contains(e.Location))return;
                grid.EndEdit();var data=new DataObject();data.SetData(dragFormat,dragSource);dragging=true;dragSelection=-1;dragScrollTimer.Start();
                try {grid.DoDragDrop(data,DragDropEffects.Move);}finally{
                    dragScrollTimer.Stop();dragging=false;dragSource=-1;dropIndex=-1;grid.Invalidate();
                    int selected=dragSelection;if(selected>=0)BeginInvoke(new Action(delegate{if(selected<grid.Rows.Count){grid.ClearSelection();grid.CurrentCell=grid.Rows[selected].Cells[1];grid.Rows[selected].Selected=true;}}));
                }
            };
            DragEventHandler over=delegate(object s,DragEventArgs e) {
                if(busy||!dragging||!e.Data.GetDataPresent(dragFormat)){e.Effect=DragDropEffects.None;return;}
                e.Effect=DragDropEffects.Move;dragPoint=grid.PointToClient(new Point(e.X,e.Y));dropIndex=DropPosition(dragPoint);dragScrollTimer.Start();grid.Invalidate();
            };
            grid.DragEnter+=over;grid.DragOver+=over;
            grid.DragLeave+=delegate{dragScrollTimer.Stop();dropIndex=-1;grid.Invalidate();};
            grid.DragDrop+=delegate(object s,DragEventArgs e) {
                if(busy||!dragging||!e.Data.GetDataPresent(dragFormat))return;
                int from=(int)e.Data.GetData(dragFormat);int insert=DropPosition(grid.PointToClient(new Point(e.X,e.Y)));
                if(from<0||from>=tracks.Count||insert<0)return;
                int to=Math.Max(0,Math.Min(tracks.Count-1,insert>from?insert-1:insert));if(to==from)return;
                var track=tracks[from];tracks.RemoveAt(from);tracks.Insert(to,track);orderBox.SelectedIndex=3;RefreshGrid(to);dragSelection=to;
                playlistNote.Text="已拖动排序；文件编号已更新，可继续拖动。";
            };
            grid.Paint+=delegate(object s,PaintEventArgs e) {
                if(!dragging||dropIndex<0||tracks.Count==0)return;
                Rectangle row=grid.GetRowDisplayRectangle(Math.Min(dropIndex,tracks.Count-1),false);
                int y=dropIndex==tracks.Count?row.Bottom:row.Top;
                if(y<grid.ColumnHeadersHeight||y>grid.ClientSize.Height)return;
                using(var pen=new Pen(accent,Math.Max(2,grid.RowTemplate.Height/12f)))e.Graphics.DrawLine(pen,grid.RowHeadersWidth,y,grid.ClientSize.Width-18,y);
            };
        }
        int DropPosition(Point p) {
            if(tracks.Count==0)return 0;
            if(p.Y<grid.ColumnHeadersHeight)return Math.Max(0,grid.FirstDisplayedScrollingRowIndex);
            int row=grid.HitTest(p.X,p.Y).RowIndex;if(row<0)return tracks.Count;
            var rect=grid.GetRowDisplayRectangle(row,false);return row+(p.Y>=rect.Top+rect.Height/2?1:0);
        }
        void ScaleGrid() {
            // Framework WinForms scales the control, but not its row template or explicit column widths.
            float factor;using(var graphics=CreateGraphics())factor=graphics.DpiX/96f;int[] widths={46,240,68,205,180,150,240,300};
            for(int i=0;i<widths.Length;i++){grid.Columns[i].MinimumWidth=(int)((i==1?180:20)*factor);if(i!=1)grid.Columns[i].Width=(int)(widths[i]*factor);}
            grid.RowHeadersWidth=(int)(30*factor);grid.ColumnHeadersHeight=(int)(36*factor);grid.RowTemplate.Height=(int)(34*factor);foreach(DataGridViewRow row in grid.Rows)row.Height=grid.RowTemplate.Height;
        }
        void SetBusy(bool value) {
            busy=value;controls.Enabled=!value;grid.ReadOnly=value;downmixBox.Enabled=!value;aiStart.Enabled=!value;
            foreach(Control c in Controls)SetButtons(c,value);cancelButton.Enabled=value;exportButton.Enabled=!value&&tracks.Count>0;openButton.Enabled=!value&&outputFolder!=null;playlistButton.Enabled=openButton.Enabled;
            if(value){cancel=new CancellationTokenSource();progress.Value=0;}else{if(cancel!=null)cancel.Dispose();cancel=null;}
        }
        void SetButtons(Control c,bool value){foreach(Control child in c.Controls){var b=child as Button;if(b!=null)b.Enabled=!value;SetButtons(child,value);}}
        void Status(string text){if(IsDisposed)return;if(InvokeRequired){BeginInvoke(new Action<string>(Status),text);return;}status.Text=text;}
        void ShowError(Exception ex){status.Text=ex.Message.Split('\r','\n')[0];MessageBox.Show(this,ex.Message,"CD Ready",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        async Task LoadFolder() {
            if(busy)return;string folder=sourceBox.Text.Trim().Trim('"');SetBusy(true);status.Text="读取音频信息…";
            try {
                tools=Tools.Find();var token=cancel.Token;var found=await Task.Run(()=>Library.Scan(folder,tools,token,Status));
                var lists=await Task.Run(()=>Playlists.Discover(folder));tracks=found;sourceBox.Text=Path.GetFullPath(folder);outputBox.Text=sourceBox.Text;
                loading=true;playlists=lists;playlistBox.Items.Clear();foreach(string list in lists)playlistBox.Items.Add(list);playlistBox.SelectedIndex=lists.Count>0?0:-1;loading=false;
                SortTracks();status.Text=tracks.Count==0?"本层文件夹没有支持的音频；NCM 需先导出为 FLAC / WAV 等格式。":"已载入 "+tracks.Count+" 首。核对曲序和曲名后，点“生成刻录文件”。";
            }catch(OperationCanceledException){status.Text="已取消读取。";}catch(Exception ex){ShowError(ex);}finally{loading=false;SetBusy(false);}
        }
        void SortTracks() {
            if(tracks.Count==0){RefreshGrid();return;}
            grid.EndEdit();int mode=orderBox.SelectedIndex;
            if(mode==3)return;
            if(mode==2)tracks=tracks.OrderBy(t=>t.Disc==0?1:t.Disc).ThenBy(t=>t.TrackNumber==0?int.MaxValue:t.TrackNumber).ThenBy(t=>t.OriginalName,new NaturalComparer()).ToList();
            else tracks=tracks.OrderBy(t=>t.OriginalName,new NaturalComparer()).ToList();
            if(mode==0&&playlistBox.SelectedIndex>=0)try {int missed;int matched=Playlists.Apply(tracks,playlists[playlistBox.SelectedIndex],out missed);playlistNote.Text="播放列表匹配 "+matched+" 首"+(missed>0?"；未匹配 "+missed+" 项":"")+"；其余接在末尾。";}catch(Exception ex){playlistNote.Text="播放列表未应用；使用文件名顺序。";ShowError(ex);}
            else playlistNote.Text=mode==0?"未发现列表；已按文件名自然排序。":"按住曲目行上下拖动；双击曲名可编辑。";
            RefreshGrid();
        }
        static string Time(double seconds){return string.Format("{0}:{1:00}",(int)seconds/60,(int)seconds%60);}
        void RefreshGrid(int selected=-1) {
            grid.Rows.Clear();for(int i=0;i<tracks.Count;i++){var t=tracks[i];int row=grid.Rows.Add((i+1).ToString("D2"),t.Title,Time(t.Duration),t.FormatText,t.PlanText,t.Naming,Names.Output(i+1,t.Title),t.OriginalName);grid.Rows[row].Cells[1].ToolTipText=t.Naming+"\r\n原文件："+t.Source;grid.Rows[row].Cells[7].ToolTipText=t.Source;if(t.Error!=null)grid.Rows[row].DefaultCellStyle.ForeColor=Color.Firebrick;}
            double secs=AudioEngine.CdSeconds(tracks);summary.Text=string.Format("{0} 首  ·  音频 {1}  ·  含间隔预留 {2} / 80:00  ·  PCM 可直接保持 {3} 首",tracks.Count,Time(tracks.Sum(t=>t.Duration)),Time(secs),tracks.Count(t=>t.Exact));summary.ForeColor=secs>4800?Color.Firebrick:accent;
            if(selected>=0&&selected<grid.Rows.Count){grid.ClearSelection();grid.Rows[selected].Selected=true;grid.CurrentCell=grid.Rows[selected].Cells[1];}exportButton.Enabled=!busy&&tracks.Count>0;
        }
        void MoveTrack(int offset) {if(busy||grid.CurrentCell==null)return;grid.EndEdit();int from=grid.CurrentCell.RowIndex,to=from+offset;if(to<0||to>=tracks.Count)return;var t=tracks[from];tracks.RemoveAt(from);tracks.Insert(to,t);orderBox.SelectedIndex=3;RefreshGrid(to);playlistNote.Text="手动顺序；编号会随顺序自动更新。";}
        void RemoveTrack() {if(busy)return;grid.EndEdit();foreach(int i in grid.SelectedRows.Cast<DataGridViewRow>().Select(r=>r.Index).OrderByDescending(x=>x))tracks.RemoveAt(i);RefreshGrid();status.Text="已从本次列表移出；原文件未删除。";}
        async Task Export() {
            if(busy)return;grid.EndEdit();try{AudioEngine.Validate(tracks,downmixBox.Checked);if(string.IsNullOrWhiteSpace(outputBox.Text))throw new IOException("请选择输出位置。");}catch(Exception ex){ShowError(ex);return;}
            var snapshot=tracks.ToList();string parent=outputBox.Text.Trim();bool mix=downmixBox.Checked;SetBusy(true);outputFolder=null;
            try {
                var token=cancel.Token;var p=new Progress<ProgressInfo>(value=>{progress.Value=value.Total==0?0:Math.Min(100,value.Done*100/value.Total);status.Text=value.Text;});
                var result=await Task.Run(()=>AudioEngine.Export(snapshot,parent,tools??Tools.Find(),mix,token,x=>((IProgress<ProgressInfo>)p).Report(x)));
                outputFolder=result.Directory;progress.Value=100;status.Text="准备完成 · "+result.Tracks.Count+" 首全部校验通过 · "+outputFolder;
                MessageBox.Show(this,"已生成 "+result.Tracks.Count+" 首，全部通过校验。\r\n\r\n"+outputFolder+"\r\n\r\n打开 WMP 列表后，把曲目添加到“音频 CD”刻录栏即可。", "准备完成",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }catch(Exception ex){ShowError(ex);}finally{SetBusy(false);}
        }
        async Task AiNames() {
            if(busy||tracks.Count==0)return;grid.EndEdit();bool mayStart=aiStart.Checked;SetBusy(true);
            try {
                var suggestions=await Task.Run(()=>LocalAi.Suggest(tracks,mayStart,cancel.Token,Status));
                using(var dialog=new RenameDialog(suggestions))if(dialog.ShowDialog(this)==DialogResult.OK){foreach(var item in dialog.Accepted){tracks[item.Index].Title=item.After;tracks[item.Index].Naming="本地 AI 建议 · 已确认";}RefreshGrid();status.Text="已应用确认的曲名；原文件未重命名。";}else status.Text="曲名建议未应用。";
            }catch(Exception ex){ShowError(ex);}finally{SetBusy(false);}
        }
        void OpenOutput(bool playlist) {if(outputFolder==null)return;try{string path=playlist?Path.Combine(outputFolder,"刻录顺序.wpl"):outputFolder;Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){ShowError(ex);}}
    }
    public class RenameDialog:Form {
        public List<NameSuggestion> Accepted=new List<NameSuggestion>();DataGridView list;IList<NameSuggestion> suggestions;
        public RenameDialog(IList<NameSuggestion> items) {
            SuspendLayout();AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            suggestions=items;Text="本地 AI 曲名建议 · 确认后应用";Size=new Size(860,520);MinimumSize=new Size(700,400);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(16),RowCount=3,ColumnCount=1};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));Controls.Add(layout);
            layout.Controls.Add(new Label {Text="只向本机模型发送文件名和标签。请核对版本词（Live / Remix / 伴奏等）；可修改建议。",Dock=DockStyle.Fill},0,0);
            list=new DataGridView {Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,BackgroundColor=Color.White,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};list.Columns.Add(new DataGridViewCheckBoxColumn {Name="use",HeaderText="应用",FillWeight=25});list.Columns.Add("before","当前曲名");list.Columns.Add("after","建议曲名（可编辑）");list.Columns.Add("note","核对状态");list.Columns[1].ReadOnly=true;list.Columns[3].ReadOnly=true;
            foreach(var item in items)list.Rows.Add(item.Before!=item.After,item.Before,item.After,item.Note);layout.Controls.Add(list,0,1);
            var row=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,9,0,0)};var ok=new Button {Text="应用勾选项",AutoSize=true};var cancel=new Button {Text="取消",DialogResult=DialogResult.Cancel,AutoSize=true};row.Controls.Add(ok);row.Controls.Add(cancel);layout.Controls.Add(row,0,2);CancelButton=cancel;
            ok.Click+=delegate{list.EndEdit();for(int i=0;i<list.Rows.Count;i++)if(Convert.ToBoolean(list.Rows[i].Cells[0].Value)){string title=Names.Clean(Convert.ToString(list.Rows[i].Cells[2].Value));if(title.Length==0){MessageBox.Show(this,"曲名不能为空。");return;}Accepted.Add(new NameSuggestion {Index=suggestions[i].Index,After=title});}DialogResult=DialogResult.OK;Close();};
            Shown+=delegate{float scale;using(var g=CreateGraphics())scale=g.DpiX/96f;list.ColumnHeadersHeight=(int)(36*scale);foreach(DataGridViewRow item in list.Rows)item.Height=(int)(34*scale);};ResumeLayout(true);
        }
    }
}
