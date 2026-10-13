using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LeigodGuard;

class IntegrationTest {
 static readonly List<string> results=new List<string>();
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);results.Add("PASS: "+label);}
 [STAThread] static void Main(string[] args) {
  if(args.Length==2&&args[0]=="--game-process"){using(var exit=EventWaitHandle.OpenExisting(args[1]))exit.WaitOne(30000);return;}
  Native.SetProcessDpiAwareness(2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  var form=new Form{Text="监控程序自测 · 独立模拟客户端",Size=new Size(1200,750),StartPosition=FormStartPosition.CenterScreen,BackColor=Color.FromArgb(25,25,25),Font=new Font("Microsoft YaHei UI",14)};
  var title=new Label{Location=new Point(35,275),Size=new Size(230,30),ForeColor=Color.White};form.Controls.Add(title);
  form.Controls.Add(new Label{Text="加速中13ms",Location=new Point(35,305),Size=new Size(230,40),Font=new Font("Microsoft YaHei UI",18),ForeColor=Color.White});
  var toggle=new Button{Text="暂停时长",Location=new Point(900,15),Size=new Size(150,45),BackColor=Color.Red,ForeColor=Color.White};int clicks=0;toggle.Click+=(a,e)=>{clicks++;toggle.Text="开启时长";};form.Controls.Add(toggle);
  form.Shown+=async(a,e)=>{
   try{
    string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lifecycle-fixture");Directory.CreateDirectory(folder);
    var names=new[]{"PUBG","APEX","PORTAL"};var files=new[]{"ArenaGame.exe","CoopGame.exe","StandaloneGame.exe"};var delays=new[]{0,5,1};
    for(int index=0;index<names.Length;index++){
     string directory=Path.Combine(folder,"Game"+index);Directory.CreateDirectory(directory);string exe=Path.Combine(directory,files[index]);File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location,exe,true);
     var binding=new GameBinding{Name=names[index],Aliases=new[]{names[index]},Processes=new string[0],Enabled=true,AutoDiscovered=true,SteamAppId=index<2?(80001+index).ToString():null,InstallDirectory=index<2?directory:null,ExecutablePath=index==2?exe:null};
     var settings=new Settings{AutoDiscover=false,FirstRunSetupCompleted=true,Games=new List<GameBinding>{binding},ExitDelaySeconds=delays[index]};settings.Validate();
     title.Text=names[index];toggle.Text="暂停时长";int beforeClicks=clicks;Native.ShowWindow(form.Handle,9);Native.SetForegroundWindow(form.Handle);await Task.Delay(400);
     var frame=await Client.ScanWindow(form.Handle,Path.Combine(folder,"ocr-"+index+".png"));
     Check(frame.Active&&frame.PauseButton!=null&&frame.ActiveRows.Any(row=>Client.Matches(row,binding)),names[index]+": real Windows OCR identifies game and billing toggle");
     var monitor=new MonitorState{Delay=delays[index]};monitor.Observe(frame,settings);Check(!monitor.Tick(Store.Processes(settings.Games),0),names[index]+": never-started game does not prompt");
     string signal="Local\\GuardTest-"+Guid.NewGuid().ToString("N");
     using(var exit=new EventWaitHandle(false,EventResetMode.ManualReset,signal)){
      using(var process=Start(exe,signal)){await WaitForProcess(settings,binding);monitor.Tick(Store.Processes(settings.Games),1);Check(monitor.Current.Seen.Contains(binding.Name),names[index]+": native path query sees running test process");exit.Set();await WaitForExit(process);}
      var stopped=Store.Processes(settings.Games);monitor.Tick(stopped,2);if(delays[index]>0)Check(!monitor.Tick(stopped,2+delays[index]-.01),names[index]+": configured delay is respected");monitor.Tick(stopped,2+delays[index]);Check(monitor.NeedsPrompt&&monitor.CanConfirm(monitor.Current.Id,stopped),names[index]+": real process exit requests confirmation");
      exit.Reset();using(var process=Start(exe,signal)){await WaitForProcess(settings,binding);Check(!monitor.CanConfirm(monitor.Current.Id,Store.Processes(settings.Games)),names[index]+": restart is rejected by fresh confirmation check");monitor.Tick(Store.Processes(settings.Games),3+delays[index]);Check(!monitor.NeedsPrompt,names[index]+": restart cancels pending reminder");exit.Set();await WaitForExit(process);}
      stopped=Store.Processes(settings.Games);monitor.Tick(stopped,4+delays[index]);monitor.Tick(stopped,4+2*delays[index]);string id=monitor.Current.Id;monitor.Decline(id);Check(!monitor.NeedsPrompt&&!monitor.Tick(stopped,50)&&clicks==beforeClicks,names[index]+": declining never clicks or repeatedly prompts");
      exit.Reset();using(var process=Start(exe,signal)){await WaitForProcess(settings,binding);monitor.Tick(Store.Processes(settings.Games),51);exit.Set();await WaitForExit(process);}
      stopped=Store.Processes(settings.Games);monitor.Tick(stopped,52);monitor.Tick(stopped,52+delays[index]);Check(monitor.NeedsPrompt,names[index]+": next genuine run and exit rearms reminder");
      Check(Native.IsWindow(form.Handle)&&frame.Window==form.Handle.ToInt64(),"click target is the test client owned by this process");
      form.Text="监控程序自测 · 等待测试窗口激活";Native.SetForegroundWindow(form.Handle);for(int focusTry=0;focusTry<300&&Native.GetForegroundWindow()!=form.Handle;focusTry++)await Task.Delay(100);Native.Click(form.Handle,frame,frame.PauseButton);form.Text="监控程序自测 · 独立模拟客户端";await Task.Delay(300);
      var after=await Client.ScanWindow(form.Handle);Check(clicks==beforeClicks+1&&after.ResumeButton!=null&&after.ResumeButton.Normalized=="开启时长",names[index]+": target click and Windows OCR verify paused state");monitor.Observe(after,settings);Check(monitor.Current==null&&!monitor.NeedsPrompt,names[index]+": verified pause clears session");
     }
    }
    results.Add("INTEGRATION TOTAL: "+results.Count+" passed; simulated clients and disposable processes only.");File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"integration-result.txt"),results);
   }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"integration-result.txt"),"FAIL: "+ex);Environment.ExitCode=1;}
   form.Close();
  };Application.Run(form);
 }
 static Process Start(string exe,string signal){return Process.Start(new ProcessStartInfo(exe,"--game-process "+signal){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});}
 static async Task WaitForProcess(Settings settings,GameBinding game){for(int retry=0;retry<40;retry++){if(Store.Processes(settings.Games).Contains(game.ProcessKeys[0]))return;await Task.Delay(50);}throw new Exception("Disposable game process did not start");}
 static async Task WaitForExit(Process process){for(int retry=0;retry<40;retry++){if(process.HasExited)return;await Task.Delay(50);}throw new Exception("Disposable game process did not exit");}
}


