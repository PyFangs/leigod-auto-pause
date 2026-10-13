using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LeigodGuard;
class OffscreenGuardForm:GuardForm {
 public System.Collections.Generic.HashSet<string> ProcessOverride;
 public int PromptCount;
 public OffscreenGuardForm(Settings settings):base(settings,true){StartPosition=FormStartPosition.Manual;Location=new Point(-10000,-10000);ShowInTaskbar=false;}
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override void OnShown(EventArgs e){}
 protected override System.Collections.Generic.HashSet<string> ReadProcessNames(){return ProcessOverride??base.ReadProcessNames();}
 protected override void OpenPrompt(){PromptCount++;}
}
class UiSettingsTests {
 static int count;
 static object Field(object o,string name){return typeof(GuardForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Check(bool c,string label){if(!c)throw new Exception("FAIL: "+label);count++;Console.WriteLine("PASS: "+label);}
 [STAThread] static void Main() {
  Native.SetProcessDpiAwareness(2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  var dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-data");Directory.CreateDirectory(dir);
  typeof(Store).GetField("Folder").SetValue(null,dir);typeof(Store).GetField("Config").SetValue(null,Path.Combine(dir,"settings.json"));typeof(Store).GetField("LogPath").SetValue(null,Path.Combine(dir,"sessions.jsonl"));
  Program.QuitSignal=new EventWaitHandle(false,EventResetMode.AutoReset);Program.ShowSignal=new EventWaitHandle(false,EventResetMode.AutoReset);
  var cfg=new TestSettings();var form=new OffscreenGuardForm(cfg);((System.Windows.Forms.Timer)Field(form,"timer")).Stop();
  var numeric=(NumericUpDown)Field(form,"delay");var state=(MonitorState)Field(form,"machine");
  state.Observe(new Frame{Window=1,PauseButton=new TextLine{Text="暂停时长"},ActiveRows=new System.Collections.Generic.List<string>{"DOTA2 加速中"}},cfg);
  state.Tick(new System.Collections.Generic.HashSet<string>(new[]{"dota2.exe"},StringComparer.OrdinalIgnoreCase),0);string id=state.Current.Id;
  Check(numeric.Minimum==0,"UI permits zero seconds");numeric.Value=1;
  Check(Store.Read<Settings>(Store.Config).ExitDelaySeconds==1&&state.Delay==1,"changing spinner saves and applies one second without Save click");
  Check(state.Current.Id==id&&state.Current.Seen.Contains("DOTA2"),"saving delay in UI preserves tracked game");
  numeric.Text="0";Check(Store.Read<Settings>(Store.Config).ExitDelaySeconds==0&&state.Delay==0,"typing zero saves and applies immediately");
  Check(((Label)Field(form,"delaySavedStatus")).Text.Contains("0 秒"),"UI displays the saved effective value");
  Check(((Label)Field(form,"workflow")).Text.Contains("立即询问"),"dashboard wording follows zero delay");
  var prompt=new ConfirmForm("DOTA2",false,state.Delay);Check(!prompt.Approved,"zero-delay real prompt still requires approval");prompt.Dispose();
  numeric.Value=1;Check(Store.Load().ExitDelaySeconds==1,"saved one-second setting survives reload");
  var grid=(DataGridView)Field(form,"games");var testGame=new GameBinding{Enabled=false,Name="测试项",Aliases=new[]{"测试项"},Processes=new[]{"test.exe"}};cfg.Games.Add(testGame);int added=grid.Rows.Add(false,"测试项","待启动");grid.Rows[added].Tag=testGame;grid.ClearSelection();grid.Rows[added].Selected=true;
  typeof(GuardForm).GetMethod("DeleteSelectedGames",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
  Check(grid.Rows.Cast<DataGridViewRow>().Count(r=>!r.IsNewRow)==1&&Convert.ToString(grid.Rows[0].Cells[1].Value)=="DOTA2","delete button removes the added row and preserves DOTA2");
  grid.ClearSelection();typeof(GuardForm).GetMethod("DeleteSelectedGames",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);Check(grid.Rows.Count==1,"deleting with no selection is harmless");
  form.Show();form.PerformLayout();Application.DoEvents();
  using(var confirmation=new OffscreenConfirmForm()){confirmation.Show();Application.DoEvents();var layout=confirmation.Controls.OfType<TableLayoutPanel>().Single();var actions=layout.Controls.OfType<TableLayoutPanel>().Single();var buttons=actions.Controls.OfType<Button>().OrderBy(b=>b.Left).ToArray();Check(buttons.Length==2&&Math.Abs(buttons[0].Width-buttons[1].Width)<=1&&buttons[0].Right<buttons[1].Left&&Math.Abs(actions.Left-(confirmation.ClientSize.Width-actions.Right))<=1,"confirmation buttons have equal width, centered group and clear spacing at current DPI");Check(confirmation.AcceptButton==null&&confirmation.CancelButton==buttons[1]&&confirmation.ActiveControl==buttons[1]&&!confirmation.Approved,"confirmation has no default approval and initially focuses Continue");using(var bitmap=new Bitmap(confirmation.Width,confirmation.Height)){confirmation.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"confirmation-layout.png"));}confirmation.Close();}
  var dialog=(ProductSettingsDialog)Field(form,"settingsDialog");dialog.PerformLayout();Check(dialog.Controls.OfType<Label>().Where(l=>l.Text=="退出等待").All(l=>!l.Bounds.IntersectsWith(numeric.Bounds)),"waiting-time label and numeric field do not overlap");
  Check(!form.Controls.OfType<TabControl>().Any()&&grid.Columns.Count==3&&!grid.Columns.Cast<DataGridViewColumn>().Any(c=>c.HeaderText.Contains("进程")||c.HeaderText.Contains("添加方式")),"main screen only shows monitor toggle, game and state without configuration tabs or addition source");
  Check(!grid.Rows[0].Cells[0].ReadOnly&&grid.Rows[0].Cells[1].ReadOnly,"monitor checkbox remains editable while game information is read-only");
  grid.Rows[0].Cells[0].Value=false;Check(!Store.Load().Games[0].Enabled,"monitor toggle saves immediately without an extra Save button");grid.Rows[0].Cells[0].Value=true;
  using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-layout.png"));}
  using(var settingsPreview=new OffscreenSettingsDialog(cfg)){settingsPreview.Show();Application.DoEvents();var collapsed=settingsPreview.ClientSize;settingsPreview.ToggleAdvanced();Application.DoEvents();var expanded=settingsPreview.ClientSize;Check(expanded.Width==collapsed.Width&&expanded.Height>collapsed.Height,"advanced settings preserve DPI-scaled width when expanded");var done=settingsPreview.Controls.OfType<Button>().Single(b=>b.Text=="完成");Check(settingsPreview.Browse.Visible&&settingsPreview.Browse.Parent.Top+settingsPreview.Browse.Bottom<done.Top&&done.Right<=settingsPreview.ClientSize.Width&&done.Bottom<=settingsPreview.ClientSize.Height,"advanced path controls and Done button are visible and do not overlap");using(var bitmap=new Bitmap(settingsPreview.Width,settingsPreview.Height)){settingsPreview.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"product-settings-advanced.png"));}settingsPreview.ToggleAdvanced();settingsPreview.ToggleAdvanced();Check(settingsPreview.ClientSize==expanded,"repeated advanced expansion does not grow or shrink dialog");settingsPreview.ToggleAdvanced();Check(settingsPreview.ClientSize==collapsed,"collapsing advanced settings restores original size");settingsPreview.Close();}
  var libraryRoot=Path.Combine(dir,"SteamFixture");var gameFolder=Path.Combine(libraryRoot,"steamapps","common","Test Game");Directory.CreateDirectory(gameFolder);var steamGame=new GameBinding{Name="测试 Steam 游戏",Aliases=new[]{"测试 Steam 游戏","Steam Test"},Processes=new string[0],SteamAppId="777",InstallDirectory=gameFolder,AutoDiscovered=true,Enabled=true};
  using(var picker=new OffscreenSteamPicker(cfg)){picker.Show();picker.SetGames(new[]{steamGame});Application.DoEvents();Check(picker.GameList.Items.Count==1&&picker.SuggestedDirectory==gameFolder,"Steam picker selects game and exposes installation directory for file browsing");picker.Search.Text="Steam Test";Check(picker.GameList.Items.Count==1,"Steam picker searches alternate game name");picker.Search.Text="777";Check(picker.GameList.Items.Count==1,"Steam picker searches Steam game id");picker.Search.Text="missing";Check(picker.GameList.Items.Count==0&&!picker.ConfirmSelection(),"empty Steam search cannot confirm stale selection");picker.Search.Text="";Application.DoEvents();using(var bitmap=new Bitmap(picker.Width,picker.Height)){picker.DrawToBitmap(bitmap,new Rectangle(0,0,picker.Width,picker.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"steam-library-picker.png"));}Check(picker.ConfirmSelection()&&picker.SelectedGame.SteamAppId=="777"&&picker.SelectedExecutable==null,"Steam picker adds game by library without requiring exe selection");}
  Check(SteamLibraryPicker.ResolveLibraryRoot(Path.Combine(libraryRoot,"steamapps","common"))==libraryRoot&&SteamLibraryPicker.ResolveLibraryRoot(Path.Combine(libraryRoot,"steamapps"))==libraryRoot,"Steam library chooser accepts root, steamapps or common directory");
  Check(SteamLibraryPicker.DefaultBrowseDirectory(null,new[]{libraryRoot})==Path.Combine(libraryRoot,"steamapps","common"),"manual add defaults to Steam common directory without a selected game");
  Check(SteamLibraryPicker.DefaultBrowseDirectory(Path.Combine(dir,"missing"),new[]{libraryRoot})==Path.Combine(libraryRoot,"steamapps","common")&&SteamLibraryPicker.DefaultBrowseDirectory(gameFolder,new[]{libraryRoot})==gameFolder,"manual add skips missing directory and prefers the selected installed game");
  var commit=typeof(GuardForm).GetMethod("AddSelectedGame",BindingFlags.Instance|BindingFlags.NonPublic);cfg.IgnoredGames=new[]{steamGame.DiscoveryKey,steamGame.Name};commit.Invoke(form,new object[]{steamGame,null,steamGame.Name,new[]{libraryRoot}});commit.Invoke(form,new object[]{steamGame,null,steamGame.Name,new[]{libraryRoot}});Check(cfg.Games.Count(g=>g.SteamAppId=="777")==1&&!cfg.IgnoredGames.Contains(steamGame.DiscoveryKey)&&cfg.SteamLibraries.Contains(libraryRoot),"explicit library add restores removed game, persists custom library and avoids duplicates");
  var lateCfg=new TestSettings{ExitDelaySeconds=0};lateCfg.Games[0].Processes=new[]{"guard-fake-game.exe"};var late=new OffscreenGuardForm(lateCfg);((System.Windows.Forms.Timer)Field(late,"timer")).Stop();var lateState=(MonitorState)Field(late,"machine");
  late.ProcessOverride=new System.Collections.Generic.HashSet<string>(new[]{"leigod.exe"},StringComparer.OrdinalIgnoreCase);
  lateState.TrackProcesses(new System.Collections.Generic.HashSet<string>(new[]{"guard-fake-game.exe"},StringComparer.OrdinalIgnoreCase),0,lateCfg);
  typeof(GuardForm).GetField("nextScan",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(late,double.MaxValue);
  typeof(GuardForm).GetField("scanTask",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(late,System.Threading.Tasks.Task.FromResult(new Frame{Window=1,PauseButton=new TextLine{Text="暂停时长"},ActiveRows=new System.Collections.Generic.List<string>{"DOTA2 加速中"}}));
  var poll=typeof(GuardForm).GetMethod("Poll",BindingFlags.Instance|BindingFlags.NonPublic);poll.Invoke(late,null);Check(late.PromptCount==1&&lateState.Current.Seen.Contains("DOTA2"),"UI polling recovers game that exited before acceleration scan completed");
  late.PromptCount=0;typeof(GuardForm).GetField("actionBusy",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(late,true);poll.Invoke(late,null);Check(late.PromptCount==0&&lateState.NeedsPrompt,"busy UI preserves pending reminder without showing it early");
  typeof(GuardForm).GetField("actionBusy",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(late,false);poll.Invoke(late,null);Check(late.PromptCount==1,"UI shows queued reminder once idle even after edge-trigger tick was missed");
  typeof(GuardForm).GetMethod("ExitGuard",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(late,null);late.Dispose();
  typeof(GuardForm).GetMethod("ExitGuard",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);form.Dispose();Program.QuitSignal.Dispose();Program.ShowSignal.Dispose();
  Console.WriteLine("UI TOTAL: "+count+" passed");
 }
}
class OffscreenSettingsDialog:ProductSettingsDialog {
 public OffscreenSettingsDialog(Settings cfg):base(cfg){Location=new Point(-10000,-10000);StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;}
 protected override bool ShowWithoutActivation{get{return true;}}
}
class OffscreenSteamPicker:SteamLibraryPicker {
 public OffscreenSteamPicker(Settings cfg):base(cfg){Location=new Point(-10000,-10000);StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;}
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override void OnShown(EventArgs e){}
}
class OffscreenConfirmForm:ConfirmForm {
 public OffscreenConfirmForm():base("测试游戏",false,0){Location=new Point(-10000,-10000);StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;TopMost=false;}
 protected override bool ShowWithoutActivation{get{return true;}}
}
