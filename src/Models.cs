using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;

namespace LeigodGuard {
public class GameBinding {
 public string Name {get;set;}
 public string[] Aliases {get;set;}
 public string[] Processes {get;set;}
 public bool Enabled {get;set;}
 public bool AutoDiscovered {get;set;}
 public bool ManualAdded {get;set;}
 public string SteamAppId {get;set;}
 public string InstallDirectory {get;set;}
 public string ExecutablePath {get;set;}
 public string DiscoveryKey {get{return !string.IsNullOrEmpty(ExecutablePath)?"exe:"+ExecutablePath:!string.IsNullOrEmpty(SteamAppId)?"steam:"+SteamAppId:Name;}}
 public bool UsesPath {get{return AutoDiscovered&&(!string.IsNullOrEmpty(InstallDirectory)||!string.IsNullOrEmpty(ExecutablePath));}}
 public string[] ProcessKeys {get{return UsesPath?new[]{"@game:"+DiscoveryKey}:Processes;}}
 public bool MatchesExecutable(string file,bool hasWindow) {
  if(!UsesPath||string.IsNullOrWhiteSpace(file)||Discovery.Auxiliary(Path.GetFileName(file)))return false;
  if(!string.IsNullOrEmpty(ExecutablePath))return string.Equals(Path.GetFullPath(file),Path.GetFullPath(ExecutablePath),StringComparison.OrdinalIgnoreCase);
  return Discovery.Inside(file,InstallDirectory)&&(Processes.Length==0||Processes.Contains(Path.GetFileName(file),StringComparer.OrdinalIgnoreCase)||hasWindow);
 }
}
public class Settings {
 public int ExitDelaySeconds {get;set;}
 public string ClientPath {get;set;}
 public List<GameBinding> Games {get;set;}
 public bool AutoDiscover {get;set;}
 public string[] IgnoredGames {get;set;}
 public string[] SteamLibraries {get;set;}
 public bool FirstRunSetupCompleted {get;set;}
 public Settings() {
  ExitDelaySeconds=5;ClientPath=@"C:\Program Files (x86)\LeiGod_Acc\leigod_launcher.exe";
  AutoDiscover=true;IgnoredGames=new string[0];SteamLibraries=new string[0];Games=new List<GameBinding>();
 }
 public void Validate() {
  if(ExitDelaySeconds<0||ExitDelaySeconds>600)throw new Exception("退出等待时间必须为 0–600 秒");
  if(Games==null)throw new Exception("游戏列表不能为空");
  foreach(var g in Games){if(string.IsNullOrWhiteSpace(g.Name)||g.Aliases==null||g.Aliases.Length==0||g.Processes==null||(g.Processes.Length==0&&!g.UsesPath))throw new Exception("请选择游戏程序，或等待自动识别安装目录");
   g.Aliases=g.Aliases.Select(x=>x.Trim()).Where(x=>x.Length>=2).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
   g.Processes=g.Processes.Select(ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
   if(g.Aliases.Length==0||g.Processes.Any(x=>string.IsNullOrWhiteSpace(x)||x.IndexOfAny(new[]{'\\','/',':','*','?'})>=0))throw new Exception("进程名只填写程序文件名");
   if(g.Processes.Any(x=>new[]{"STEAM.EXE","LEIGOD.EXE","STEAMWEBHELPER.EXE","EXPLORER.EXE"}.Contains(x.ToUpperInvariant())))throw new Exception("请选择游戏进程，不要填写 Steam、雷神或桌面进程");
  }
 }
 public static string ProcessName(string value){var s=(value??"").Trim();return s.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?s:s+".exe";}
}
public class MonitorSession {
 public string Id=Guid.NewGuid().ToString("N");
 public DateTime StartUtc=DateTime.UtcNow;
 public List<GameBinding> Games;
 public HashSet<string> Seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 public HashSet<string> Running=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 public bool UnknownGame,Suppressed,PromptPending;
 public bool PauseFailed;
 public double? ExitAt;
 public string Fingerprint;
}
public class MonitorState {
 class ProcessObservation {
  public GameBinding Game;
  public bool Running;
  public double? ExitedAt;
  public string ConsumedBy;
  public int ReviewAttempts;
  public double LastReview;
  public DateTime LastChangeUtc;
 }
 readonly Dictionary<string,ProcessObservation> observations=new Dictionary<string,ProcessObservation>(StringComparer.OrdinalIgnoreCase);
 Settings trackedSettings;
 bool clientAvailable;
 const double RecoveryWindow=30;
 static string GameKey(GameBinding game){return game.Name+":"+string.Join(",",game.ProcessKeys.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase));}
 public MonitorSession Current;
 public string Status="等待雷神启动";
 public event Action<string,MonitorSession> Changed;
 public event Action<string,GameBinding> ProcessChanged;
 public int Delay=5;
 public bool NeedsPrompt {get{return Current!=null&&Current.PromptPending&&!Current.Suppressed;}}
 public void TrackProcesses(HashSet<string> processes,double now,Settings settings,bool clientRunning=true) {
  trackedSettings=settings;clientAvailable=clientRunning;
  if(!clientRunning){observations.Clear();return;}
  var enabled=settings.Games.Where(g=>g.Enabled).ToList();var keys=new HashSet<string>(enabled.Select(GameKey),StringComparer.OrdinalIgnoreCase);
  foreach(var key in observations.Keys.Where(k=>!keys.Contains(k)).ToArray())observations.Remove(key);
  foreach(var game in enabled) {
   string key=GameKey(game);ProcessObservation seen;if(!observations.TryGetValue(key,out seen)){seen=new ProcessObservation{Game=game};observations.Add(key,seen);}
   bool running=game.ProcessKeys.Any(processes.Contains);
   if(running&&!seen.Running){seen.ExitedAt=null;seen.ConsumedBy=null;seen.ReviewAttempts=0;seen.LastChangeUtc=DateTime.UtcNow;if(ProcessChanged!=null)ProcessChanged("process_started",game);}
   if(!running&&seen.Running){seen.ExitedAt=now;seen.LastChangeUtc=DateTime.UtcNow;if(ProcessChanged!=null)ProcessChanged("process_exited",game);}
   seen.Running=running;
  }
 }
 bool Recoverable(ProcessObservation seen,double now){return seen.ExitedAt.HasValue&&now-seen.ExitedAt.Value<=RecoveryWindow&&seen.ConsumedBy==null;}
 public bool NeedsClientReview(double now) {
  return clientAvailable&&Current==null&&observations.Values.Any(s=>Recoverable(s,now)&&s.ReviewAttempts<3&&(s.ReviewAttempts==0||now-s.LastReview>=10));
 }
 public void BeginClientReview(double now) {
  foreach(var seen in observations.Values.Where(s=>Recoverable(s,now))){seen.ReviewAttempts++;seen.LastReview=now;}
 }
 public void UpdateDelay(int seconds,double now) {
  if(seconds<0||seconds>600)throw new ArgumentOutOfRangeException("seconds");
  Delay=seconds;
  if(Current!=null&&Current.PromptPending&&Current.ExitAt.HasValue&&now-Current.ExitAt.Value<seconds)Current.PromptPending=false;
 }
 public void Observe(Frame f,Settings settings) {
  trackedSettings=settings;clientAvailable=f.Window!=0;
  if(f.Window==0){observations.Clear();End("client_closed");Status="等待雷神启动";return;}
  if(f.ResumeButton!=null){
   // OCR completion can lag behind a process transition; a pre-exit frame cannot erase that exit.
   if(f.CapturedUtc!=default(DateTime)&&observations.Values.Any(s=>s.LastChangeUtc>f.CapturedUtc))return;
   foreach(var seen in observations.Values.Where(s=>!s.Running)){seen.ExitedAt=null;seen.ConsumedBy=null;}End("manually_paused");Status="雷神时长已暂停";return;
  }
  if(!f.Active){if(Current==null)Status=f.Error??"等待开始游戏加速";return;}
  var games=settings.Games.Where(g=>g.Enabled&&f.ActiveRows.Any(row=>Client.Matches(row,g))).ToList();
  bool unknown=f.ActiveRows.Any(row=>!games.Any(g=>Client.Matches(row,g)));
  string fp=string.Join("|",games.Select(g=>g.Name+":"+string.Join(",",g.ProcessKeys)).OrderBy(x=>x))+"#"+(unknown?string.Join("|",f.ActiveRows.Where(row=>!games.Any(g=>Client.Matches(row,g)))):"");
  if(Current==null||Current.Fingerprint!=fp) {
   var previous=Current;var retained=previous==null?new string[0]:games.Where(g=>previous.Games.Any(old=>GameKey(old)==GameKey(g))&&previous.Seen.Contains(g.Name)).Select(g=>g.Name).ToArray();
   End("acceleration_changed");Current=new MonitorSession{Games=games,Fingerprint=fp,UnknownGame=unknown};foreach(var name in retained)Current.Seen.Add(name);Raise("session_started");
  }
 }
 public bool Tick(HashSet<string> processNames,double now) {
  if(trackedSettings!=null&&clientAvailable)TrackProcesses(processNames,now,trackedSettings);
  var s=Current;if(s==null)return false;
  s.Running=new HashSet<string>(s.Games.Where(g=>g.ProcessKeys.Any(p=>processNames.Contains(p))).Select(g=>g.Name),StringComparer.OrdinalIgnoreCase);
  foreach(var game in s.Games) {
   ProcessObservation seen;observations.TryGetValue(GameKey(game),out seen);
   bool running=s.Running.Contains(game.Name);
   if(running||(seen!=null&&Recoverable(seen,now))) {
    if(s.Seen.Add(game.Name))Raise(running?"game_started":"game_start_recovered");
    if(seen!=null)seen.ConsumedBy=s.Id;
   }
  }
  if(s.Running.Count>0) {
   if(s.ExitAt.HasValue||s.PromptPending||s.Suppressed)Raise("game_restarted");
   s.ExitAt=null;s.Suppressed=false;s.PromptPending=false;s.PauseFailed=false;Status="监控中："+string.Join("、",s.Running);return false;
  }
  if(s.UnknownGame){s.ExitAt=null;Status="有未绑定的加速游戏；请在设置中添加游戏进程";return false;}
  if(s.Seen.Count==0){Status="等待游戏首次启动："+string.Join("、",s.Games.Select(g=>g.Name));return false;}
  if(s.Suppressed){Status=s.PauseFailed?"暂停未完成；请检查雷神后从托盘重试":"你已选择继续加速；游戏下次运行并退出后再提醒";return false;}
  if(!s.ExitAt.HasValue){var exits=s.Games.Where(g=>s.Seen.Contains(g.Name)).Select(g=>{ProcessObservation seen;return observations.TryGetValue(GameKey(g),out seen)?seen.ExitedAt:null;}).Where(t=>t.HasValue).ToArray();s.ExitAt=exits.Length==0?now:exits.Max(t=>t.Value);Raise("game_exited");}
  var left=Delay-(now-s.ExitAt.Value);Status=left>0?"游戏已退出，"+Math.Ceiling(left)+" 秒后询问是否暂停":"等待你确认是否暂停时长";
  if(left<=0&&!s.PromptPending){s.PromptPending=true;Raise("confirmation_requested");return true;}
  return false;
 }
 public bool CanConfirm(string id,HashSet<string> processes) {return Current!=null&&Current.Id==id&&Current.PromptPending&&!Current.UnknownGame&&!Current.Games.Any(g=>g.ProcessKeys.Any(p=>processes.Contains(p)));}
 public void Decline(string id){if(Current!=null&&Current.Id==id){Current.Suppressed=true;Current.PromptPending=false;Raise("user_declined");}}
 public void Failed(string id){if(Current!=null&&Current.Id==id){Current.Suppressed=true;Current.PromptPending=false;Current.PauseFailed=true;Raise("pause_incomplete");}}
 public void End(string reason){if(Current==null)return;Raise(reason);Current=null;}
 void Raise(string kind){if(Changed!=null)Changed(kind,Current);}
}
public static class ReminderText {
 public static string Detail() {
  return "是否暂停雷神的计费时长？";
 }
 public static string Workflow(int seconds) {
  return "流程：识别加速游戏 → 等待游戏运行 → "+(seconds==0?"退出后立即询问":"退出满 "+seconds+" 秒")+" → 你确认 → 暂停时长\n游戏重启会取消提醒；忽略或关闭提醒不会暂停。";
 }
}
}
