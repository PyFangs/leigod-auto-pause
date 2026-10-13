using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LeigodGuard {
public class ConfirmForm:Form {
 public bool Approved;
 public bool AutomaticDismissal;
 public Label Detail;
 public ConfirmForm(string names,bool test,int seconds) {
  SuspendLayout();Text=test?"测试提醒（不会暂停雷神）":"游戏已退出 · 是否暂停雷神时长？";ClientSize=new Size(440,test?190:168);StartPosition=FormStartPosition.CenterScreen;
  TopMost=true;ShowInTaskbar=true;Font=new Font("Microsoft YaHei UI",10);BackColor=Color.White;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
  AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),RowCount=2,ColumnCount=1};layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));Controls.Add(layout);
  Detail=new Label{Text=test?"这是一条测试提醒，确认也不会操作雷神。\n当前退出等待："+seconds+" 秒。":ReminderText.Detail(),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Margin=new Padding(0,0,0,14)};layout.Controls.Add(Detail,0,0);
  var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,16));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));layout.Controls.Add(actions,0,1);
  var pause=new Button{Text=test?"测试确认":"暂停时长",Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Color.FromArgb(33,111,80),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};pause.FlatAppearance.BorderSize=0;
  var keep=new Button{Text="继续加速",Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Color.White,FlatStyle=FlatStyle.Flat};keep.FlatAppearance.BorderColor=Color.FromArgb(205,211,216);
  pause.Click+=(s,e)=>{Approved=true;Close();};keep.Click+=(s,e)=>{Approved=false;Close();};actions.Controls.Add(pause,0,0);actions.Controls.Add(keep,2,0);CancelButton=keep;ActiveControl=keep;ResumeLayout(true);
  // No default affirmative button and no timeout: silence can never approve a pause.
 }
}
public class GuardForm:Form {
 Settings settings;
 readonly MonitorState machine=new MonitorState();
 readonly Stopwatch clock=Stopwatch.StartNew();
 readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=500};
 NotifyIcon tray;
 Label status,ocrStatus,startupStatus,workflow,delaySavedStatus,bindingHelp;
 ProductSettingsDialog settingsDialog;
 Button allowButton;
 bool refreshingGames,syncStartup;
 Task<DiscoveryResult> discoveryTask;
 double nextDiscovery;
 double lastUnknownDiscovery=-100;
 DataGridView games;
 TextBox clientPath;
 NumericUpDown delay;
 ListBox history;
 Task<Frame> scanTask;
 Frame lastFrame;
 double nextScan;
 bool background,exiting,actionBusy,syncDelay;
 ConfirmForm prompt;
 string promptSession,requestPath;
 string lastStatus;
 public GuardForm(Settings s,bool bg) {
  SuspendLayout();
  settings=s;background=bg;machine.Delay=s.ExitDelaySeconds;
  Text="雷神自动暂停助手";ClientSize=new Size(720,490);MinimumSize=new Size(640,450);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(245,247,250);Icon=SystemIcons.Shield;
  AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
  var page=new Panel{Dock=DockStyle.Fill,Padding=new Padding(24)};Controls.Add(page);
  var header=new Panel{Dock=DockStyle.Top,Height=56};page.Controls.Add(header);
  header.Controls.Add(new Label{Text="雷神自动暂停助手",AutoSize=true,Font=new Font("Microsoft YaHei UI",18,FontStyle.Bold),Location=new Point(0,0)});
  var settingsButton=new Button{Text="设置",Size=new Size(84,34),Location=new Point(582,0),Anchor=AnchorStyles.Top|AnchorStyles.Right,FlatStyle=FlatStyle.Flat,BackColor=Color.White};settingsButton.FlatAppearance.BorderColor=Color.FromArgb(218,224,230);header.Controls.Add(settingsButton);settingsButton.Click+=(a,b)=>OpenSettings();header.Resize+=(a,b)=>settingsButton.Left=header.ClientSize.Width-settingsButton.Width;
  var card=new Panel{Dock=DockStyle.Top,Height=96,BackColor=Color.White,Padding=new Padding(18)};page.Controls.Add(card);card.BringToFront();
  status=new Label{Text="正在准备监控",Location=new Point(18,14),Size=new Size(500,36),Font=new Font("Microsoft YaHei UI",17,FontStyle.Bold),ForeColor=Color.FromArgb(32,87,65),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};card.Controls.Add(status);
  ocrStatus=new Label{Text="",Location=new Point(20,57),Size=new Size(530,25),ForeColor=Color.FromArgb(104,114,125),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};card.Controls.Add(ocrStatus);
  allowButton=new Button{Text="允许监控",Location=new Point(532,29),Size=new Size(116,36),Anchor=AnchorStyles.Top|AnchorStyles.Right,Visible=false};card.Controls.Add(allowButton);allowButton.Click+=(a,b)=>Upgrade();
  var footer=new Panel{Dock=DockStyle.Bottom,Height=40};page.Controls.Add(footer);
  bindingHelp=new Label{Text="正在自动识别本机游戏…",Dock=DockStyle.Fill,TextAlign=ContentAlignment.BottomLeft,ForeColor=Color.FromArgb(104,114,125)};footer.Controls.Add(bindingHelp);
  var uninstall=new Button{Text="卸载",Width=60,Dock=DockStyle.Right,FlatStyle=FlatStyle.Flat,ForeColor=Color.DimGray,Font=new Font(Font.FontFamily,9)};uninstall.FlatAppearance.BorderSize=0;footer.Controls.Add(uninstall);uninstall.Click+=(a,b)=>Uninstall();
  var listPanel=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,18,0,0)};page.Controls.Add(listPanel);listPanel.BringToFront();
  var listHeader=new Panel{Dock=DockStyle.Top,Height=38};listPanel.Controls.Add(listHeader);
  listHeader.Controls.Add(new Label{Text="已添加的游戏",AutoSize=true,Location=new Point(0,6),Font=new Font(Font,FontStyle.Bold)});
  var add=new LinkLabel{Text="添加游戏",AutoSize=true,Location=new Point(589,6),Anchor=AnchorStyles.Top|AnchorStyles.Right,LinkColor=Color.FromArgb(33,111,80)};listHeader.Controls.Add(add);add.LinkClicked+=(a,b)=>SelectGame();listHeader.Resize+=(a,b)=>add.Left=listHeader.ClientSize.Width-add.Width;
  games=new DataGridView{Dock=DockStyle.Fill,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,ReadOnly=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,GridColor=Color.FromArgb(237,240,244),EnableHeadersVisualStyles=false,ColumnHeadersHeight=36};
  games.DefaultCellStyle.SelectionBackColor=Color.FromArgb(232,243,237);games.DefaultCellStyle.SelectionForeColor=Color.FromArgb(35,45,55);games.DefaultCellStyle.Padding=new Padding(6);games.RowTemplate.Height=40;games.ColumnHeadersDefaultCellStyle.BackColor=Color.White;games.ColumnHeadersDefaultCellStyle.ForeColor=Color.FromArgb(104,114,125);games.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
  games.Columns.Add(new DataGridViewCheckBoxColumn{Name="Enabled",HeaderText="监控",Width=82,AutoSizeMode=DataGridViewAutoSizeColumnMode.None,ReadOnly=false});games.Columns.Add("Game","游戏");games.Columns.Add(new DataGridViewTextBoxColumn{Name="State",HeaderText="状态",FillWeight=65});listPanel.Controls.Add(games);games.BringToFront();
  foreach(DataGridViewColumn column in games.Columns)if(column.Index>0)column.ReadOnly=true;
  var context=new ContextMenuStrip();context.Items.Add("移除游戏",null,(a,b)=>DeleteSelectedGames());games.ContextMenuStrip=context;games.MouseDown+=(a,b)=>{if(b.Button==MouseButtons.Right){var hit=games.HitTest(b.X,b.Y);if(hit.RowIndex>=0){games.ClearSelection();games.Rows[hit.RowIndex].Selected=true;}}};
  games.CurrentCellDirtyStateChanged+=(a,b)=>{if(games.IsCurrentCellDirty)games.CommitEdit(DataGridViewDataErrorContexts.Commit);};
  games.CellValueChanged+=(a,b)=>{if(refreshingGames||b.RowIndex<0||b.ColumnIndex!=0)return;var game=games.Rows[b.RowIndex].Tag as GameBinding;if(game==null)return;game.Enabled=Convert.ToBoolean(games.Rows[b.RowIndex].Cells[0].Value);SaveSettings();if(machine.Current!=null&&machine.Current.Games.Contains(game)){CancelRequest();machine.End("settings_changed");}nextScan=0;};
  settingsDialog=new ProductSettingsDialog(s);delay=settingsDialog.Delay;clientPath=settingsDialog.ClientPath;delaySavedStatus=settingsDialog.Saved;startupStatus=new Label();workflow=new Label();
  delay.ValueChanged+=(a,b)=>SaveDelayImmediately();delay.TextChanged+=(a,b)=>{int value;if(!syncDelay&&int.TryParse(delay.Text,out value)&&value>=0&&value<=600)delay.Value=value;};
  settingsDialog.AutoDiscover.CheckedChanged+=(a,b)=>{settings.AutoDiscover=settingsDialog.AutoDiscover.Checked;SaveSettings();nextDiscovery=0;};
  settingsDialog.Startup.CheckedChanged+=(a,b)=>{if(syncStartup)return;try{AdminCommand(settingsDialog.Startup.Checked?"--install-startup":"--remove-startup",settingsDialog.Startup.Checked);}catch(Exception e){MessageBox.Show(settingsDialog,e.Message,"未能更改开机启动");RefreshStartup();}};
  settingsDialog.Browse.Click+=(a,b)=>SelectClient();clientPath.Leave+=(a,b)=>SaveSettings();settingsDialog.Diagnostics.LinkClicked+=(a,b)=>ShowDiagnostics();
  history=new ListBox{HorizontalScrollbar=true,Font=new Font("Consolas",9)};
  machine.Changed+=(kind,session)=>{var detail=new{session=session==null?null:session.Id,games=session==null?new string[0]:session.Games.Select(g=>g.Name).ToArray(),processes=session==null?new string[0]:session.Games.SelectMany(g=>g.ProcessKeys).ToArray(),seen=session==null?new string[0]:session.Seen.ToArray()};Store.Log(kind,detail);AddHistory(DateTime.Now.ToString("HH:mm:ss")+" "+kind+" "+string.Join(", ",detail.games));};
  machine.ProcessChanged+=(kind,game)=>Store.Log(kind,new{game=game.Name,processes=game.ProcessKeys});
  var menu=new ContextMenuStrip();menu.Items.Add("打开",null,(a,b)=>ShowWindow());menu.Items.Add("设置",null,(a,b)=>OpenSettings());menu.Items.Add("暂停雷神时长…",null,(a,b)=>ManualPause());menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出",null,(a,b)=>ExitGuard());tray=new NotifyIcon{Icon=SystemIcons.Shield,Text="雷神自动暂停助手",Visible=true,ContextMenuStrip=menu};tray.DoubleClick+=(a,b)=>ShowWindow();
  RebuildGames();Shown+=(a,b)=>{if(background)HideToTray();else{ShowWindow();settings.MainWindowShown=true;Store.Write(Store.Config,settings);}RefreshStartup();};FormClosing+=(a,b)=>{if(!exiting){if(b.CloseReason!=CloseReason.UserClosing){PrepareExit();return;}b.Cancel=true;CloseToTray(true);}};timer.Tick+=(a,b)=>Poll();timer.Start();Store.Log("monitor_started",new{admin=Store.IsAdmin,delay=machine.Delay,version="2.4"});ResumeLayout(true);
 }
 void AddButton(Control parent,string text,int x,int y,int w,Action action) {var b=new Button{Text=text,Location=new Point(x,y),Size=new Size(w,38)};b.Click+=(a,e)=>{try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"雷神自动暂停助手",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};parent.Controls.Add(b);}
 void Uninstall(){if(actionBusy){MessageBox.Show(this,"请等待当前暂停操作完成后再卸载。","卸载助手");return;}if(MessageBox.Show(this,"将移除开机启动，删除助手程序及本地设置和记录，并关闭助手。\n\n是否继续卸载？","卸载雷神自动暂停助手",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;try{using(var child=UninstallAction.Start()){ExitGuard();}}catch(System.ComponentModel.Win32Exception e){if(e.NativeErrorCode!=1223)MessageBox.Show(this,e.Message,"未能卸载");}catch(Exception e){MessageBox.Show(this,e.Message,"未能卸载");}}
 void AddHistory(string text){history.Items.Insert(0,text);if(history.Items.Count>180)history.Items.RemoveAt(history.Items.Count-1);}
 void RefreshStartup(){syncStartup=true;try{settingsDialog.Startup.Checked=Startup.Enabled();}finally{syncStartup=false;}}
 void ShowWindow(){Show();WindowState=FormWindowState.Normal;ShowInTaskbar=true;Native.ShowWindow(Handle,9);Activate();RefreshStartup();}
 void HideToTray(){Hide();ShowInTaskbar=false;}
 protected virtual void ShowTrayNotice(){MessageBox.Show(this,"关闭窗口后，助手会继续在托盘监控游戏。\n\n双击任务栏右下角的助手图标，可以重新打开主窗口。\n要完全关闭程序，请右键托盘图标并选择“退出”。","助手将继续在后台运行",MessageBoxButtons.OK,MessageBoxIcon.Information);}
 protected void CloseToTray(bool userClosing){if(userClosing&&!settings.TrayNoticeShown){ShowTrayNotice();settings.TrayNoticeShown=true;Store.Write(Store.Config,settings);}HideToTray();}
 void PrepareExit(){CancelRequest();exiting=true;timer.Stop();if(prompt!=null){prompt.Approved=false;prompt.Close();}tray.Visible=false;tray.Dispose();}
 void ExitGuard(){PrepareExit();Close();}
 void Upgrade(){var child=Store.Elevate("--takeover",true);if(child!=null)ExitGuard();}
 void AdminCommand(string args,bool exit){var child=Store.Elevate(args);if(child!=null&&exit)ExitGuard();}
 void LaunchClient(){if(!File.Exists(settings.ClientPath))throw new Exception("请在设置中选择雷神启动程序");Process.Start(new ProcessStartInfo(settings.ClientPath){UseShellExecute=true});nextScan=0;}
 void CancelRequest(){if(requestPath!=null){try{File.WriteAllText(requestPath+".cancel","cancelled");}catch{}}}
 protected virtual HashSet<string> ReadProcessNames(){return Store.Processes(settings.Games);}
 void Poll() {
  try {
   if(Program.QuitSignal.WaitOne(0)){ExitGuard();return;}if(Program.ShowSignal.WaitOne(0))ShowWindow();
   if(Program.RestoreSignal!=null&&Program.RestoreSignal.WaitOne(0)&&!actionBusy)RestorePreview();
   var processes=ReadProcessNames();double now=clock.Elapsed.TotalSeconds;
   machine.TrackProcesses(processes,now,settings,processes.Contains("leigod.exe"));
   if(!processes.Contains("leigod.exe")){machine.End("client_closed");machine.Status="等待雷神启动";lastFrame=null;}
   if(scanTask!=null&&scanTask.IsCompleted&&!actionBusy){var f=scanTask.GetAwaiter().GetResult();scanTask=null;if(processes.Contains("leigod.exe")){lastFrame=f;machine.Observe(lastFrame,settings);if(settings.AutoDiscover&&f.Active&&machine.Current!=null&&machine.Current.UnknownGame&&now-lastUnknownDiscovery>=8){lastUnknownDiscovery=now;nextDiscovery=0;}}}
   if(scanTask==null&&now>=nextScan&&processes.Contains("leigod.exe")&&Native.DesktopReady()&&!actionBusy) {nextScan=now+4;scanTask=Task.Run(()=>Client.Scan());}
   machine.Tick(processes,now);
   if(prompt!=null&&(machine.Current==null||!machine.CanConfirm(promptSession,processes))){prompt.AutomaticDismissal=true;prompt.Approved=false;prompt.Close();prompt=null;CancelRequest();}
   if(requestPath!=null&&!actionBusy)requestPath=null;
   if(requestPath!=null&&!string.IsNullOrEmpty(promptSession)&&!machine.CanConfirm(promptSession,processes))CancelRequest();
   PollDiscovery(now);RenderStatus(processes);UpdateGameStates(processes);
   if(lastStatus!=machine.Status){lastStatus=machine.Status;tray.Text=machine.Status.Length>60?machine.Status.Substring(0,60):machine.Status;
    try{Store.Write(Path.Combine(Path.GetDirectoryName(Store.Exe),"运行状态.json"),new{updated=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),status=machine.Status,exitDelaySeconds=machine.Delay,admin=Store.IsAdmin,games=machine.Current==null?new string[0]:machine.Current.Games.Select(g=>g.Name).ToArray(),seen=machine.Current==null?new string[0]:machine.Current.Seen.ToArray(),promptPending=machine.NeedsPrompt,session=machine.Current==null?null:machine.Current.Id,localFolder=Store.Folder});}catch{}
   }
   if(machine.NeedsPrompt&&!actionBusy&&prompt==null)OpenPrompt();
   if(!actionBusy&&prompt==null&&machine.NeedsClientReview(now)){machine.BeginClientReview(now);RestorePreview();}
  }catch(Exception e){status.Text="监控遇到问题："+e.Message;}
 }
 protected virtual void OpenPrompt() {
  var s=machine.Current;if(s==null)return;promptSession=s.Id;
  var f=new ConfirmForm(string.Join("、",s.Seen),false,machine.Delay);prompt=f;
  f.FormClosed+=async(a,e)=>{
   if(prompt==f)prompt=null;var id=s.Id;
   if(f.Approved&&machine.CanConfirm(id,Store.Processes(settings.Games)))await ExecutePause(false,id);
   else if(!f.AutomaticDismissal)machine.Decline(id);
   f.Dispose();
  };f.Show();f.Activate();
 }
 void TestPrompt(){using(var f=new ConfirmForm("测试游戏",true,machine.Delay)){f.ShowDialog();}MessageBox.Show("测试完成，没有操作雷神。","测试提醒");}
 async void ManualPause(){if(actionBusy)return;using(var f=new ConfirmForm("",false,machine.Delay)){f.Detail.Text="是否现在暂停雷神的计费时长？\n这会影响当前账号正在加速的游戏。";f.ShowDialog();if(f.Approved)await ExecutePause(true,null);}}
 async Task ExecutePause(bool manual,string id) {
  if(actionBusy)return;actionBusy=true;promptSession=id;
  try {
   if(scanTask!=null){await scanTask;scanTask=null;}
   if(!manual&&!machine.CanConfirm(id,Store.Processes(settings.Games)))throw new Exception("游戏或加速状态已改变，取消暂停");
   var req=new PauseRequest{SessionId=id,ExpiresUtc=DateTime.UtcNow.AddMinutes(2),Settings=settings,Fingerprint=manual?null:machine.Current.Fingerprint,Manual=manual,GameProcesses=manual?new string[0]:machine.Current.Games.SelectMany(g=>g.ProcessKeys).ToArray()};
   requestPath=Path.Combine(Store.Folder,"requests",Guid.NewGuid().ToString("N")+".json");Store.Write(requestPath,req);Store.Log("user_confirmed",new{session=id,manual=manual});
   PauseResult result;
   uint pid;var h=Native.FindClient();Native.GetWindowThreadProcessId(h,out pid);
   if(!Store.IsAdmin&&Native.Elevated(pid)) {
    var child=await Task.Run(()=>Store.Elevate("--pause-once "+Store.Quote(requestPath)));
    if(child==null)throw new Exception("没有启动暂停操作");
    await Task.Run(()=>{using(child){if(!child.WaitForExit(45000))throw new Exception("暂停操作超时，请检查雷神界面");}});
    result=Store.Read<PauseResult>(requestPath+".result");
   }else result=await PauseAction.Run(req,requestPath);
   if(result.Success){machine.End("paused");machine.Status="雷神时长已暂停";status.Text=result.Message;AddHistory(DateTime.Now.ToString("HH:mm:ss")+" "+result.Message);}
   else {machine.Failed(id);MessageBox.Show(result.Message,"请检查雷神",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
  }catch(Exception e){machine.Failed(id);Store.Log("pause_cancelled",new{session=id,error=e.Message});MessageBox.Show(e.Message,"未暂停雷神",MessageBoxButtons.OK,MessageBoxIcon.Information);}
  finally {actionBusy=false;requestPath=null;nextScan=0;}
 }
 async void RestorePreview() {
  actionBusy=true;
  try {
   Store.Log("client_review_started",null);
   if(scanTask!=null){await scanTask;scanTask=null;}
   var frame=await Client.RestoreAndRead(false,()=>{if(!Native.DesktopReady())throw new Exception("桌面已锁定");},false);
   lastFrame=frame;machine.Observe(frame,settings);
   Store.Log("client_review_completed",new{pause=frame.PauseButton!=null,resume=frame.ResumeButton!=null,games=frame.ActiveRows,error=frame.Error});
   Store.Write(Path.Combine(Path.GetDirectoryName(Store.Exe),"恢复检查.json"),new{success=true,updated=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),pause=frame.PauseButton!=null,resume=frame.ResumeButton!=null,frame.ActiveRows,frame.Error});
  }catch(Exception e){Store.Log("client_review_failed",new{error=e.Message});Store.Write(Path.Combine(Path.GetDirectoryName(Store.Exe),"恢复检查.json"),new{success=false,error=e.Message});}
  finally {actionBusy=false;nextScan=0;}
 }
 void OpenSettings(){RefreshStartup();settingsDialog.ShowDialog(this);SaveSettings();}
 void ShowDiagnostics(){using(var form=new Form{Text="诊断信息",Size=new Size(660,420),StartPosition=FormStartPosition.CenterParent,Font=Font}){var text=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Text="版本：2.4\r\n"+"当前状态："+machine.Status+"\r\n"+"本地记录："+Store.LogPath+"\r\n\r\n"+string.Join("\r\n",history.Items.Cast<string>())};form.Controls.Add(text);form.ShowDialog(this);}}
 void SelectGame(){using(var picker=new SteamLibraryPicker(settings)){if(picker.ShowDialog(this)!=DialogResult.OK)return;AddSelectedGame(picker.SelectedGame,picker.SelectedExecutable,picker.SelectedName,picker.AddedLibraries);}}
 void AddSelectedGame(GameBinding steam,string file,string title,IEnumerable<string> libraries){
  GameBinding candidate;
  if(file!=null)candidate=new GameBinding{Name=title,Aliases=(steam==null?new string[0]:steam.Aliases).Concat(new[]{title}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),Processes=new[]{Path.GetFileName(file)},Enabled=true,AutoDiscovered=true,ManualAdded=true,ExecutablePath=Path.GetFullPath(file),SteamAppId=steam==null?null:steam.SteamAppId};
  else candidate=new GameBinding{Name=title,Aliases=steam.Aliases.Concat(new[]{title}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),Processes=steam.Processes.ToArray(),Enabled=true,AutoDiscovered=true,ManualAdded=true,InstallDirectory=steam.InstallDirectory,SteamAppId=steam.SteamAppId};
  var existing=settings.Games.FirstOrDefault(g=>(candidate.SteamAppId!=null&&g.SteamAppId==candidate.SteamAppId)||(file!=null&&g.MatchesExecutable(file,true))||g.DiscoveryKey==candidate.DiscoveryKey);
  bool changedMonitor=false;
  if(existing==null)settings.Games.Add(candidate);
  else{var previous=string.Join("|",existing.ProcessKeys);existing.Aliases=existing.Aliases.Concat(candidate.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();existing.Enabled=true;if(file!=null){existing.ExecutablePath=candidate.ExecutablePath;existing.Processes=candidate.Processes;existing.AutoDiscovered=true;existing.ManualAdded=true;}changedMonitor=previous!=string.Join("|",existing.ProcessKeys);}
  var restored=new HashSet<string>(candidate.Aliases.Concat(new[]{candidate.DiscoveryKey,candidate.Name}).Concat(existing==null?new string[0]:new[]{existing.DiscoveryKey,existing.Name}),StringComparer.OrdinalIgnoreCase);
  settings.IgnoredGames=(settings.IgnoredGames??new string[0]).Where(key=>!restored.Contains(key)).ToArray();settings.SteamLibraries=(settings.SteamLibraries??new string[0]).Concat(libraries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  SaveSettings();RebuildGames();nextScan=0;if(changedMonitor&&machine.Current!=null&&machine.Current.Games.Contains(existing)){CancelRequest();machine.End("settings_changed");}
 }
 void SelectClient(){using(var d=new OpenFileDialog{Filter="启动程序 (*.exe)|*.exe"}){if(d.ShowDialog()==DialogResult.OK){clientPath.Text=d.FileName;SaveSettings();}}}
 void SaveSettings(){settings.ExitDelaySeconds=(int)delay.Value;settings.ClientPath=clientPath.Text.Trim();settings.Validate();Store.Write(Store.Config,settings);ApplySettings(settings);}
 void SaveDelayImmediately(){if(syncDelay)return;int seconds=(int)delay.Value;if(seconds==settings.ExitDelaySeconds)return;int before=settings.ExitDelaySeconds;try{settings.ExitDelaySeconds=seconds;settings.Validate();Store.Write(Store.Config,settings);ApplySettings(settings);Store.Log("exit_delay_changed",new{seconds=seconds});}catch(Exception e){settings.ExitDelaySeconds=before;delaySavedStatus.Text="保存失败："+e.Message;syncDelay=true;try{delay.Value=before;}finally{syncDelay=false;}}}
 void ApplySettings(Settings changed){settings=changed;machine.UpdateDelay(changed.ExitDelaySeconds,clock.Elapsed.TotalSeconds);workflow.Text=ReminderText.Workflow(machine.Delay);delaySavedStatus.Text="已保存 · 当前退出等待 "+machine.Delay+" 秒";lastStatus=null;if(prompt!=null&&machine.Current!=null){if(!machine.Current.PromptPending){prompt.AutomaticDismissal=true;prompt.Close();prompt=null;}else prompt.Detail.Text=ReminderText.Detail();}}
 void DeleteSelectedGames(){var selected=games.SelectedRows.Cast<DataGridViewRow>().Select(r=>r.Tag as GameBinding).Where(g=>g!=null).ToArray();if(selected.Length==0)return;bool active=machine.Current!=null&&selected.Any(machine.Current.Games.Contains);foreach(var game in selected){settings.IgnoredGames=(settings.IgnoredGames??new string[0]).Concat(new[]{game.DiscoveryKey,game.Name}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();settings.Games.Remove(game);}SaveSettings();if(active){CancelRequest();machine.End("settings_changed");}nextScan=0;RebuildGames();}
 void RebuildGames(){refreshingGames=true;try{games.Rows.Clear();foreach(var game in settings.Games.OrderBy(g=>g.Name,StringComparer.CurrentCultureIgnoreCase)){int row=games.Rows.Add(game.Enabled,game.Name,"待启动");games.Rows[row].Tag=game;}games.ClearSelection();}finally{refreshingGames=false;}bindingHelp.Text=settings.AutoDiscover?"新游戏会自动加入 · 关闭窗口后继续后台监控":"关闭窗口后继续后台监控";}
 protected virtual DiscoveryResult DiscoverGames(){return Discovery.Scan(Discovery.SteamRoots().Concat(settings.SteamLibraries??new string[0]),Discovery.CacheFolders());}
 void PollDiscovery(double now){
  if(discoveryTask!=null&&discoveryTask.IsCompleted){var completed=discoveryTask;discoveryTask=null;try{var found=completed.GetAwaiter().GetResult();if(settings.AutoDiscover&&Discovery.Merge(settings,found)){settings.Validate();Store.Write(Store.Config,settings);RebuildGames();nextScan=0;if(lastFrame!=null&&lastFrame.Active&&!actionBusy)machine.Observe(lastFrame,settings);Store.Log("games_discovered",new{count=settings.Games.Count,steam=found.InstalledSteamGames,mappings=found.LocalMappings});}}catch(Exception e){Store.Log("discovery_failed",new{error=e.Message});bindingHelp.Text="暂未识别游戏；可以选择游戏程序添加。";}}
  if(discoveryTask==null&&settings.AutoDiscover&&now>=nextDiscovery){nextDiscovery=now+60;discoveryTask=Task.Run(()=>DiscoverGames());}
 }
 void UpdateGameStates(HashSet<string> processes){foreach(DataGridViewRow row in games.Rows){var game=row.Tag as GameBinding;if(game==null)continue;string state=!game.Enabled?"已关闭":game.ProcessKeys.Any(processes.Contains)?"运行中":machine.Current!=null&&machine.Current.Seen.Contains(game.Name)?"已退出":"待启动";if(!Equals(row.Cells[2].Value,state))row.Cells[2].Value=state;}}
 void RenderStatus(HashSet<string> processes){
  string title,detail="";uint pid;Native.GetWindowThreadProcessId(Native.FindClient(),out pid);bool permission=!Store.IsAdmin&&Native.Elevated(pid);allowButton.Visible=permission;
  if(permission){title="需要允许后台监控";detail="允许后即可自动识别和暂停雷神。";}
  else if(actionBusy)title="正在确认雷神状态";
  else if(!processes.Contains("leigod.exe"))title="等待雷神启动";
  else if(machine.Current==null){title=lastFrame!=null&&lastFrame.ResumeButton!=null?"时长已暂停":"等待加速游戏";if(lastFrame!=null&&lastFrame.Error!=null&&lastFrame.Error.Contains("缺少文字识别")){title="需要中文文字识别组件";detail="请在 Windows 语言设置中安装中文文字识别。";}}
  else if(machine.Current.Running.Count>0){title="正在监控游戏";detail=string.Join(" · ",machine.Current.Running);}
  else if(machine.Current.UnknownGame){title="发现新的加速游戏";detail=settings.AutoDiscover?"正在自动识别；未能识别时可选择游戏程序。":"请添加这款游戏。";}
  else if(machine.Current.Suppressed){title=machine.Current.PauseFailed?"暂停未完成":"继续加速中";detail=machine.Current.PauseFailed?"请检查雷神界面。":"下次游戏退出时再提醒。";}
  else if(machine.NeedsPrompt)title="等待暂停确认";
  else if(machine.Current.Seen.Count>0){title="游戏已退出";detail=machine.Status;}
  else{title="等待游戏启动";detail=string.Join(" · ",machine.Current.Games.Select(g=>g.Name));}
  status.Text=title;ocrStatus.Text=detail;
 }
}
}







