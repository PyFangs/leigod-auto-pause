using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LeigodGuard {
public static class Store {
 public static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LeigodGuard");
 public static readonly string Config=Path.Combine(Folder,"settings.json");
 public static readonly string LogPath=Path.Combine(Folder,"sessions.jsonl");
 public static readonly string Exe=System.Reflection.Assembly.GetExecutingAssembly().Location;
 public static string Sid {get{return WindowsIdentity.GetCurrent().User.Value;}}
 public static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
 static readonly object Gate=new object();
 public static bool IsAdmin {get{return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);}}
 public static void Write(string path,object value) {lock(Gate){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,Json.Serialize(value),System.Text.Encoding.UTF8);}}
 public static T Read<T>(string path) {lock(Gate){return Json.Deserialize<T>(File.ReadAllText(path,System.Text.Encoding.UTF8));}}
 public static Settings Load() {Directory.CreateDirectory(Folder);if(!File.Exists(Config)){var s=new Settings();Write(Config,s);return s;}var loaded=Read<Settings>(Config);loaded.Validate();if(!Read<Dictionary<string,object>>(Config).ContainsKey("FirstRunSetupCompleted")){loaded.FirstRunSetupCompleted=true;Write(Config,loaded);}return loaded;}
 public static void Log(string kind,object details) {lock(Gate){Directory.CreateDirectory(Folder);if(File.Exists(LogPath)&&new FileInfo(LogPath).Length>2*1024*1024){File.Copy(LogPath,LogPath+".previous",true);File.WriteAllText(LogPath,"");}File.AppendAllText(LogPath,Json.Serialize(new{time=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),kind=kind,details=details})+Environment.NewLine,System.Text.Encoding.UTF8);}}
 public static HashSet<string> Processes(IEnumerable<GameBinding> bindings=null) {var games=(bindings??new GameBinding[0]).Where(g=>g.Enabled&&g.UsesPath).ToArray();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var p in Process.GetProcesses()){using(p){try{string name=Settings.ProcessName(p.ProcessName);names.Add(name);if(games.Length==0||Discovery.Auxiliary(name))continue;var path=Native.ProcessPath((uint)p.Id);if(path==null)continue;bool window=p.MainWindowHandle!=IntPtr.Zero;foreach(var game in games)if(game.MatchesExecutable(path,window))names.Add(game.ProcessKeys[0]);}catch{}}}return names;}
 public static string Fingerprint(Frame f,Settings s) {
  var gs=s.Games.Where(g=>g.Enabled&&f.ActiveRows.Any(r=>Client.Matches(r,g))).ToList();
  var unknown=f.ActiveRows.Where(r=>!gs.Any(g=>Client.Matches(r,g))).ToArray();
  return string.Join("|",gs.Select(g=>g.Name+":"+string.Join(",",g.ProcessKeys)).OrderBy(x=>x))+"#"+string.Join("|",unknown);
 }
 public static Process Elevate(string arguments) {return Process.Start(new ProcessStartInfo(Exe,arguments){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});}
 public static string Quote(string s) {if(s.Contains("\""))throw new Exception("路径中不能包含双引号");return "\""+s+"\"";}
}
public class PauseRequest {
 public string SessionId {get;set;}
 public DateTime ExpiresUtc {get;set;}
 public Settings Settings {get;set;}
 public string Fingerprint {get;set;}
 public bool Manual {get;set;}
 public string[] GameProcesses {get;set;}
}
public class PauseResult {public bool Success {get;set;}public string Message {get;set;}}
public static class PauseAction {
 static void Recheck(PauseRequest req,string path) {
  if(DateTime.UtcNow>req.ExpiresUtc||File.Exists(path+".cancel"))throw new Exception("确认已失效，游戏或加速状态发生了变化");
  if(!req.Manual){var processes=Store.Processes(req.Settings.Games);if(req.GameProcesses.Any(processes.Contains))throw new Exception("游戏已重新启动，已取消暂停");}
  if(!Native.DesktopReady())throw new Exception("桌面已锁定，已取消暂停");
 }
 public static async Task<PauseResult> Run(PauseRequest req,string path) {
  bool clicked=false;
  try {
   req.Settings.Validate();var f=await Client.RestoreAndRead(!req.Manual,()=>Recheck(req,path));var h=new IntPtr(f.Window);
   if(f.ResumeButton!=null){Store.Log("pause_already_paused",new{session=req.SessionId,button=f.ResumeButton.Text});return new PauseResult{Success=true,Message="雷神时长已经暂停"};}
   if(f.PauseButton==null)throw new Exception(f.Error??"未找到暂停时长按钮");
   if(!req.Manual&&(!f.Active||Store.Fingerprint(f,req.Settings)!=req.Fingerprint))throw new Exception("当前加速游戏已变更，已取消暂停");
   Recheck(req,path);Native.Click(h,f,f.PauseButton);clicked=true;
   for(int i=0;i<7;i++) {
    await Task.Delay(550);var after=await Client.Scan(null,true);if(after.Window!=h.ToInt64())throw new Exception("暂停后的雷神窗口发生变化，请检查暂停状态");
    if(after.ResumeButton!=null||after.Lines.Any(l=>l.Normalized=="时长已暂停"||l.Normalized=="暂停成功")) {
     Store.Log("pause_verified",new{session=req.SessionId,button=after.ResumeButton==null?null:after.ResumeButton.Text});return new PauseResult{Success=true,Message="已确认雷神时长暂停成功"};
    }
    // Do not click an unverified secondary dialog or toggle the main button again.
   }
   throw new Exception("已点击暂停，但暂未识别到暂停状态。\n请检查雷神右上角：显示“开启时长”且时间停止变化表示已暂停。\n如果仍显示“暂停时长”，请手动暂停；如有雷神自己的确认窗口，请完成确认。");
  }catch(Exception e){Store.Log("pause_failed",new{session=req.SessionId,error=e.Message,clicked=clicked});return new PauseResult{Success=false,Message=e.Message};}
 }
}
public static class Startup {
 public static string TaskName {get{return "LeigodGuard-"+Store.Sid;}}
 public static void Install(bool restart=true) {
  dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));service.Connect();dynamic folder=service.GetFolder("\\");
  dynamic definition=service.NewTask(0);definition.RegistrationInfo.Description="雷神本地游戏退出提醒；确认后通过界面暂停时长。";
  definition.Principal.UserId=Store.Sid;definition.Principal.LogonType=3;definition.Principal.RunLevel=1;
  definition.Settings.Enabled=true;definition.Settings.Hidden=false;definition.Settings.StartWhenAvailable=true;
  definition.Settings.DisallowStartIfOnBatteries=false;definition.Settings.StopIfGoingOnBatteries=false;definition.Settings.ExecutionTimeLimit="PT0S";definition.Settings.MultipleInstances=2;
  dynamic trigger=definition.Triggers.Create(9);trigger.UserId=Store.Sid;trigger.Delay="PT8S";
  dynamic action=definition.Actions.Create(0);action.Path=Store.Exe;action.Arguments="--background";action.WorkingDirectory=Path.GetDirectoryName(Store.Exe);
  try {dynamic old=folder.GetTask(TaskName);if(!string.Equals((string)old.Definition.Actions.Item(1).Path,Store.Exe,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("同名启动任务指向其他程序，未覆盖");}
  catch(FileNotFoundException){}
  catch(System.Runtime.InteropServices.COMException e){if((uint)e.ErrorCode!=0x80070002)throw;}
  dynamic task=folder.RegisterTaskDefinition(TaskName,definition,6,Store.Sid,null,3,null);Store.Log("startup_enabled",new{path=Store.Exe});
  if(restart){Program.SignalQuit();Thread.Sleep(1800);task.Run(null);}
 }
 public static bool Enabled() {try{dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));service.Connect();dynamic task=service.GetFolder("\\").GetTask(TaskName);return (bool)task.Enabled;}catch{return false;}}
 public static void Remove() {
  dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));service.Connect();dynamic folder=service.GetFolder("\\");
  dynamic task=folder.GetTask(TaskName);if(!string.Equals((string)task.Definition.Actions.Item(1).Path,Store.Exe,StringComparison.OrdinalIgnoreCase))throw new Exception("启动任务指向其他程序，未移除");
  folder.DeleteTask(TaskName,0);Store.Log("startup_disabled",null);
 }
}
public static class Program {
 public static EventWaitHandle QuitSignal,ShowSignal,RestoreSignal;
 public static void SignalQuit(){try{using(var e=EventWaitHandle.OpenExisting("Local\\LeigodGuard-Quit-"+Store.Sid))e.Set();}catch{}}
 [STAThread] public static void Main(string[] args) {
  try {Native.SetProcessDpiAwareness(2);}catch{}
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  try {
   if(args.Contains("--quit")){SignalQuit();return;}
   if(args.Contains("--restore-client")){using(var signal=EventWaitHandle.OpenExisting("Local\\LeigodGuard-Restore-"+Store.Sid))signal.Set();return;}
   if(args.Length>0&&args[0]=="--pause-once") {
    if(args.Length!=2)throw new Exception("缺少确认记录");string path=Path.GetFullPath(args[1]);
    if(!path.StartsWith(Path.Combine(Store.Folder,"requests")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("确认记录路径无效");
    var req=Store.Read<PauseRequest>(path);Store.Write(path+".result",PauseAction.Run(req,path).GetAwaiter().GetResult());return;
   }
   if(args.Contains("--install-startup")){if(!Store.IsAdmin){Store.Elevate("--install-startup");return;}Startup.Install();return;}
   if(args.Contains("--remove-startup")){if(!Store.IsAdmin){Store.Elevate("--remove-startup");return;}Startup.Remove();return;}
   if(args.Contains("--diagnose")){var f=Client.Scan().GetAwaiter().GetResult();Store.Write(Path.Combine(Store.Folder,"diagnostic.json"),new{f.Window,f.Width,f.Height,f.Active,f.ActiveRows,pause=f.PauseButton!=null,resume=f.ResumeButton!=null,f.Minimized,f.Error,admin=Store.IsAdmin});return;}
   Settings settings=Store.Load();if(!File.Exists(settings.ClientPath)){var client=Discovery.DetectClientPath();if(!string.IsNullOrEmpty(client)){settings.ClientPath=client;Store.Write(Store.Config,settings);}}if(args.Contains("--launch-client")){if(File.Exists(settings.ClientPath))Process.Start(new ProcessStartInfo(settings.ClientPath){UseShellExecute=true});else MessageBox.Show("请先在设置中选择雷神启动程序。","雷神自动暂停助手");}
   if(args.Contains("--takeover")){SignalQuit();Thread.Sleep(1800);}
   var mutexSecurity=new MutexSecurity();mutexSecurity.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(Store.Sid),MutexRights.FullControl,AccessControlType.Allow));
   bool created;using(var mutex=new Mutex(false,"Local\\LeigodGuard-"+Store.Sid,out created,mutexSecurity)) {
    bool acquired=false;try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
    if(!acquired){if(!args.Contains("--background")){try{using(var e=EventWaitHandle.OpenExisting("Local\\LeigodGuard-Show-"+Store.Sid))e.Set();}catch(Exception e){Store.Log("ipc_show_failed",new{error=e.Message});}}return;}
    var eventSecurity=new EventWaitHandleSecurity();eventSecurity.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(Store.Sid),EventWaitHandleRights.FullControl,AccessControlType.Allow));
    QuitSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\LeigodGuard-Quit-"+Store.Sid,out created,eventSecurity);
    ShowSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\LeigodGuard-Show-"+Store.Sid,out created,eventSecurity);
    RestoreSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\LeigodGuard-Restore-"+Store.Sid,out created,eventSecurity);
    if(FirstRunSetup.Run(settings,args.Contains("--first-run-setup"),Store.IsAdmin,args.Contains("--background"),s=>Store.Write(Store.Config,s),arguments=>{using(var child=Store.Elevate(arguments)){return child!=null;}},()=>Startup.Install(false),(kind,message)=>{Store.Log(kind,new{message=message});if(kind=="first_run_setup_failed")MessageBox.Show("未能启用开机后台运行，可在设置中重试。\n"+message,"雷神自动暂停助手",MessageBoxButtons.OK,MessageBoxIcon.Warning);})){QuitSignal.Dispose();ShowSignal.Dispose();RestoreSignal.Dispose();mutex.ReleaseMutex();return;}
    Store.Log("monitor_initializing",new{admin=Store.IsAdmin,background=args.Contains("--background")});
    try{using(QuitSignal)using(ShowSignal)using(RestoreSignal)Application.Run(new GuardForm(settings,args.Contains("--background")));}finally{mutex.ReleaseMutex();}
   }
  }catch(Exception e){try{Store.Log("program_error",new{error=e.Message,trace=e.ToString()});}catch{}MessageBox.Show(e.Message,"雷神自动暂停助手",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
 }
}
}
