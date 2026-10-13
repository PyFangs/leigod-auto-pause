using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using LeigodGuard;
class Tests {
 static int count;
 static HashSet<string> Processes(params string[] names){return new HashSet<string>(names,StringComparer.OrdinalIgnoreCase);}
 static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);count++;}
 static Frame Active(params string[] rows){return new Frame{Window=1,PauseButton=new TextLine{Text="暂停时长"},ActiveRows=rows.ToList()};}
 static MonitorState New(Settings settings){var m=new MonitorState{Delay=5};m.Observe(Active("DOTA2 东南亚服 加速中13ms"),settings);return m;}
 static void Main() {
  var cfg=new TestSettings();cfg.Validate();var m=New(cfg);
  Check(!m.Tick(Processes("steam.exe"),100)&&m.Current.Seen.Count==0,"game never started must not prompt");
  Check(!m.Tick(Processes("dota2.exe","steam.exe"),101)&&m.Current.Seen.Contains("DOTA2"),"track actual game process");
  Check(!m.Tick(Processes("steam.exe"),110),"begin exit countdown while Steam remains");
  Check(!m.Tick(Processes("steam.exe"),114.999),"no prompt before five seconds");
  Check(m.Tick(Processes("steam.exe"),115),"prompt exactly at five seconds after observed exit");
  Check(!m.Tick(Processes("steam.exe"),116),"no duplicate prompts");
  string id=m.Current.Id;Check(m.CanConfirm(id,Processes("steam.exe")),"confirmation allowed for unchanged stopped game");
  Check(!m.CanConfirm(id,Processes("dota2.exe")),"fresh process check rejects restart");
  m.Decline(id);Check(!m.Tick(Processes("steam.exe"),500),"decline never repeats during same absence");
  m.Tick(Processes("dota2.exe"),501);m.Tick(Processes(),502);Check(m.Tick(Processes(),507),"restart rearms next exit reminder");
  m=New(cfg);m.Tick(Processes("dota2.exe"),1);m.Tick(Processes(),2);m.Tick(Processes("dota2.exe"),6.9);Check(!m.Tick(Processes("dota2.exe"),10),"restart during countdown cancels reminder");
  m.Tick(Processes(),11);m.Tick(Processes(),16);id=m.Current.Id;m.Observe(new Frame{Window=1,ResumeButton=new TextLine{Text="恢复时长"}},cfg);Check(m.Current==null&&!m.CanConfirm(id,Processes()),"manual time pause invalidates pending confirmation");
  m=New(cfg);m.Tick(Processes("dota2.exe"),1);m.Tick(Processes(),2);m.Observe(new Frame{Window=0},cfg);Check(m.Current==null&&!m.Tick(Processes(),10),"client exit clears monitoring");
  m=New(cfg);m.Tick(Processes("dota2.exe"),1);m.Observe(new Frame{Window=1,Error="minimized"},cfg);m.Tick(Processes(),2);Check(m.Tick(Processes(),7),"minimized window preserves recorded active session");
  m=New(cfg);m.Observe(Active("DOTA2 加速中","未知游戏 加速中"),cfg);m.Tick(Processes("dota2.exe"),1);m.Tick(Processes(),2);Check(!m.Tick(Processes(),30)&&m.Current.UnknownGame,"unknown active game prevents premature global pause");
  cfg.Games.Add(new GameBinding{Name="Test",Aliases=new[]{"TESTGAME"},Processes=new[]{"game.exe"},Enabled=true});m=new MonitorState{Delay=5};m.Observe(Active("DOTA2 加速中","TESTGAME 加速中"),cfg);m.Tick(Processes("dota2.exe","game.exe"),1);Check(!m.Tick(Processes("game.exe"),10)&&!m.Tick(Processes("game.exe"),30),"another accelerated game running prevents reminder");m.Tick(Processes(),31);Check(m.Tick(Processes(),36),"all accelerated games exited prompts once");
  id=m.Current.Id;m.Observe(Active("DOTA2 加速中"),cfg);Check(m.Current.Id!=id&&!m.CanConfirm(id,Processes()),"acceleration switch invalidates stale confirmation");
  cfg.Games[0].Processes=new[]{"steam.exe"};bool invalid=false;try{cfg.Validate();}catch{invalid=true;}Check(invalid,"reject launcher process as game binding");
  cfg=new TestSettings{ExitDelaySeconds=0};cfg.Validate();Check(cfg.ExitDelaySeconds==0,"zero delay is a valid setting");
  m=New(cfg);m.UpdateDelay(0,0);Check(!m.Tick(Processes(),1),"zero delay still waits for first game start");m.Tick(Processes("dota2.exe"),2);Check(m.Tick(Processes(),3),"zero delay prompts on first exit observation");
  id=m.Current.Id;Check(!m.CanConfirm(id,Processes("dota2.exe")),"zero delay still cancels on restart");m.Decline(id);Check(!m.Tick(Processes(),4),"zero delay never bypasses user decline");
  cfg=new TestSettings();m=New(cfg);m.Tick(Processes("dota2.exe"),1);m.Tick(Processes(),2);id=m.Current.Id;m.UpdateDelay(1,2.2);Check(m.Current.Id==id&&m.Current.Seen.Count==1,"changing delay preserves the observed session");Check(!m.Tick(Processes(),2.999)&&m.Tick(Processes(),3),"one-second delay really triggers after one second");
  m.UpdateDelay(10,3.1);Check(!m.Current.PromptPending&&!m.Current.Suppressed,"increasing delay withdraws prompt without declining");Check(!m.Tick(Processes(),11.999)&&m.Tick(Processes(),12),"increased delay uses original exit time");
  m=New(cfg);m.Tick(Processes("dota2.exe"),1);m.Tick(Processes(),2);m.UpdateDelay(0,2.1);Check(m.Tick(Processes(),2.1),"changing an active countdown to zero prompts immediately");
  Check(ReminderText.Workflow(0).Contains("立即询问"),"zero-delay workflow is explicit");
  invalid=false;try{new TestSettings{ExitDelaySeconds=-1}.Validate();}catch{invalid=true;}Check(invalid,"negative delay remains invalid");
  var paused=new Frame{Window=1,Width=1800,Height=1120,Lines=new List<TextLine>{new TextLine{Text="开启时长",X=1400,Y=25,W=120,H=28}}};Client.IdentifyButtons(paused);
  Check(paused.ResumeButton!=null&&paused.PauseButton==null&&!paused.Active,"actual client 开启时长 means billing paused");
  m=New(new TestSettings());m.Tick(Processes("dota2.exe"),1);m.Tick(Processes(),2);m.Tick(Processes(),7);m.Observe(paused,new TestSettings());Check(m.Current==null&&m.Status=="雷神时长已暂停","actual paused state clears pending reminder");
  var gameButton=new Frame{Width=1800,Height=1120,Lines=new List<TextLine>{new TextLine{Text="开启时长",X=400,Y=600},new TextLine{Text="开始加速",X=1400,Y=25}}};Client.IdentifyButtons(gameButton);Check(gameButton.ResumeButton==null&&gameButton.PauseButton==null,"game launch or out-of-region text is not a billing toggle");
  var runningButton=new Frame{Width=1800,Height=1120,Lines=new List<TextLine>{new TextLine{Text="暂停时长",X=1400,Y=25}}};Client.IdentifyButtons(runningButton);Check(runningButton.PauseButton!=null&&runningButton.ResumeButton==null,"active billing toggle remains correctly identified");
  foreach(var name in new[]{"恢复时长","恢复时间","开始时长","开 启 时 长"}){paused.Lines[0].Text=name;Client.IdentifyButtons(paused);Check(paused.ResumeButton!=null,"paused toggle recognized: "+name);}
  m=New(new TestSettings());m.Tick(Processes("dota2.exe"),1);m.Tick(Processes(),2);m.Tick(Processes(),7);m.Failed(m.Current.Id);Check(!m.Tick(Processes(),8)&&m.Status.Contains("暂停未完成")&&!m.Status.Contains("选择继续加速"),"pause failure is not misreported as user decline");
  m.Tick(Processes("dota2.exe"),9);m.Tick(Processes(),10);Check(m.Tick(Processes(),15),"game restart rearms monitoring after pause failure");
  cfg=new TestSettings();m=new MonitorState{Delay=5};m.Observe(new Frame{Window=1,Error="OCR not ready"},cfg);m.Tick(Processes("dota2.exe"),1);m.Tick(Processes(),2);
  Check(m.Current==null&&m.NeedsClientReview(2),"game exit before acceleration OCR requests automatic review");
  m.Observe(Active("DOTA2 加速中"),cfg);Check(!m.Tick(Processes(),3)&&m.Current.Seen.Contains("DOTA2")&&m.Current.ExitAt==2,"late acceleration OCR recovers actual start and original exit time");
  Check(!m.Tick(Processes(),6.999)&&m.Tick(Processes(),7),"recovered exit uses real five-second countdown without restarting it");
  Check(!m.Tick(Processes(),8)&&m.NeedsPrompt,"pending prompt remains queued while UI is temporarily busy");m.Decline(m.Current.Id);Check(!m.NeedsPrompt,"user decline clears a queued prompt");
  m.End("changed");m.Observe(Active("DOTA2 加速中"),cfg);Check(!m.Tick(Processes(),9)&&m.Current.Seen.Count==0,"consumed game episode cannot trigger a second acceleration session");
  m=new MonitorState{Delay=0};m.TrackProcesses(Processes("dota2.exe"),1,cfg);m.TrackProcesses(Processes(),2,cfg);m.Observe(new Frame{Window=1,Minimized=true},cfg);Check(m.NeedsClientReview(3),"hidden client with independent game-exit record is reviewed automatically");m.Observe(Active("DOTA2 加速中"),cfg);Check(m.Tick(Processes(),3)&&m.CanConfirm(m.Current.Id,Processes()),"hidden-client recovery prompts immediately at zero delay");
  m=new MonitorState{Delay=0};m.TrackProcesses(Processes("dota2.exe"),1,cfg);m.TrackProcesses(Processes(),2,cfg);m.Observe(new Frame{Window=1,ResumeButton=new TextLine{Text="开启时长"}},cfg);m.Observe(Active("DOTA2 加速中"),cfg);Check(!m.Tick(Processes(),3)&&!m.NeedsPrompt,"confirmed paused state discards exited game history");
  m=new MonitorState{Delay=0};m.TrackProcesses(Processes("dota2.exe"),1,cfg);m.TrackProcesses(Processes(),2,cfg);m.TrackProcesses(Processes("dota2.exe"),3,cfg);m.Observe(Active("DOTA2 加速中"),cfg);Check(!m.Tick(Processes("dota2.exe"),4)&&m.Current.Seen.Contains("DOTA2"),"restart before late OCR prevents a stale exit reminder");Check(m.Tick(Processes(),5),"fresh exit after late OCR rearms zero-delay reminder");
  m=new MonitorState();m.TrackProcesses(Processes("dota2.exe"),1,cfg);m.TrackProcesses(Processes(),2,cfg);m.BeginClientReview(2);Check(!m.NeedsClientReview(3)&&m.NeedsClientReview(12),"failed automatic review retries with spacing");m.BeginClientReview(12);m.BeginClientReview(22);Check(!m.NeedsClientReview(23),"automatic review has a bounded retry count");
  m=new MonitorState{Delay=0};m.TrackProcesses(Processes("dota2.exe"),1,cfg);m.TrackProcesses(Processes(),2,cfg);m.Observe(Active("DOTA2 加速中"),cfg);Check(!m.Tick(Processes(),33)&&m.Current.Seen.Count==0,"old unconfirmed game history expires without a surprise prompt");
  m=new MonitorState{Delay=0};m.TrackProcesses(Processes("dota2.exe"),1,cfg);m.TrackProcesses(Processes(),2,cfg);m.TrackProcesses(Processes(),3,cfg,false);m.Observe(Active("DOTA2 加速中"),cfg);Check(!m.Tick(Processes(),4)&&m.Current.Seen.Count==0,"client shutdown clears independent process history");
  m=new MonitorState{Delay=0};var staleTime=DateTime.UtcNow.AddSeconds(-1);m.TrackProcesses(Processes("dota2.exe"),1,cfg);m.TrackProcesses(Processes(),2,cfg);m.Observe(new Frame{Window=1,CapturedUtc=staleTime,ResumeButton=new TextLine{Text="开启时长"}},cfg);Check(m.NeedsClientReview(3),"delayed pre-exit paused screenshot cannot erase a newer process exit");m.Observe(Active("DOTA2 加速中"),cfg);Check(m.Tick(Processes(),3),"new active frame still recovers exit after a stale paused screenshot");
  Console.WriteLine("TOTAL: "+count+" passed");
 }
}
