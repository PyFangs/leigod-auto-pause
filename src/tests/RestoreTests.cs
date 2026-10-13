using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using LeigodGuard;
class RestoreTests {
 static int count;
 static Frame Running(bool game=true){return new Frame{Window=1,PauseButton=new TextLine{Text="暂停时长"},ActiveRows=game?new List<string>{"DOTA2 加速中"}:new List<string>()};}
 static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
 static Task NoWait(){return Task.FromResult(0);}
 [STAThread] static void Main(){Run().GetAwaiter().GetResult();Console.WriteLine("RESTORE TOTAL: "+count+" passed");}
 static async Task Run(){
  var frames=new Queue<Frame>(new[]{new Frame{Window=1,Minimized=true},new Frame{Window=1,Error="black frame"},Running(false),Running()});int reads=0,checks=0;
  var result=await Client.WaitForReady(()=>{reads++;return Task.FromResult(frames.Dequeue());},()=>checks++,NoWait,true);
  Check(result.Active&&reads==4,"tray restore waits through hidden, black and incomplete game frames");
  Check(checks==12,"request validity is checked before wait, after wait and after each scan");
  reads=0;result=await Client.WaitForReady(()=>{reads++;return Task.FromResult(new Frame{Window=1,ResumeButton=new TextLine{Text="开启时长"}});},()=>{},NoWait,true);
  Check(result.ResumeButton!=null&&reads==1,"already paused restore returns without requiring a game row");
  result=await Client.WaitForReady(()=>Task.FromResult(Running(false)),()=>{},NoWait,false);Check(result.PauseButton!=null,"manual pause can read billing toggle without an active game");
  reads=0;bool failed=false;try{await Client.WaitForReady(()=>{reads++;return Task.FromResult(new Frame{Window=1,Error="black frame"});},()=>{},NoWait,true,3);}catch(Exception e){failed=e.Message.Contains("尚未点击暂停");}Check(failed&&reads==3,"persistent black frame stops after bounded retries without a click");
  reads=0;checks=0;failed=false;try{await Client.WaitForReady(()=>{reads++;return Task.FromResult(Running());},()=>{if(++checks==3)throw new Exception("game restarted");},NoWait,true);}catch(Exception e){failed=e.Message=="game restarted";}Check(failed&&reads==1,"game restart after capture cancels before ready frame is accepted");
  reads=0;checks=0;failed=false;try{await Client.WaitForReady(()=>{reads++;return Task.FromResult(Running());},()=>{if(++checks==2)throw new Exception("locked desktop");},NoWait,true);}catch(Exception e){failed=e.Message=="locked desktop";}Check(failed&&reads==0,"lock during restore cancels before capture");
  using(var hidden=new Form()){var handle=hidden.Handle;var frame=await Client.ScanWindow(handle);Check(frame.Minimized&&frame.PauseButton==null&&frame.Lines.Count==0,"tray-hidden native window is rejected before capture and OCR");}
 }
}
