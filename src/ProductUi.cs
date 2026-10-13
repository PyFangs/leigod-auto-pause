using System;
using System.Drawing;
using System.Windows.Forms;

namespace LeigodGuard {
public class ProductSettingsDialog:Form {
 public NumericUpDown Delay;
 public CheckBox AutoDiscover,Startup;
 public TextBox ClientPath;
 public Label Saved;
 public LinkLabel Advanced,Diagnostics;
 public Button Browse;
 Button done;
 Panel pathPanel;
 int collapsedHeight;
 public ProductSettingsDialog(Settings settings) {
  SuspendLayout();
  Text="设置";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(440,330);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;BackColor=Color.White;
  AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
  AutoDiscover=new CheckBox{Text="自动添加本机游戏",Checked=settings.AutoDiscover,Location=new Point(24,26),AutoSize=true};Controls.Add(AutoDiscover);
  Startup=new CheckBox{Text="开机后在后台运行",Location=new Point(24,76),AutoSize=true};Controls.Add(Startup);
  Controls.Add(new Label{Text="退出等待",Location=new Point(24,130),AutoSize=true});
  Delay=new NumericUpDown{Minimum=0,Maximum=600,Value=settings.ExitDelaySeconds,Location=new Point(126,125),Width=80};Controls.Add(Delay);
  Controls.Add(new Label{Text="秒",Location=new Point(216,130),AutoSize=true});
  Saved=new Label{Text="0 秒表示游戏退出后立即询问",Location=new Point(24,172),Size=new Size(380,30),ForeColor=Color.DimGray};Controls.Add(Saved);
  Advanced=new LinkLabel{Text="高级设置",Location=new Point(24,224),AutoSize=true};Controls.Add(Advanced);
  Diagnostics=new LinkLabel{Text="诊断信息",Location=new Point(310,224),AutoSize=true};Controls.Add(Diagnostics);
  pathPanel=new Panel{Location=new Point(24,266),Size=new Size(392,86),Visible=false};Controls.Add(pathPanel);
  pathPanel.Controls.Add(new Label{Text="雷神启动程序",Location=new Point(0,0),AutoSize=true});ClientPath=new TextBox{Text=settings.ClientPath,Location=new Point(0,30),Width=308};pathPanel.Controls.Add(ClientPath);Browse=new Button{Text="选择",Location=new Point(318,28),Size=new Size(72,32)};pathPanel.Controls.Add(Browse);
  Advanced.LinkClicked+=(a,b)=>ToggleAdvanced();
  done=new Button{Text="完成",Size=new Size(98,34),Location=new Point(318,282)};Controls.Add(done);done.Click+=(a,b)=>Close();CancelButton=done;ResumeLayout(true);
 }
 public void ToggleAdvanced(){if(collapsedHeight==0)collapsedHeight=ClientSize.Height;bool expand=!pathPanel.Visible;pathPanel.Visible=expand;Advanced.Text=expand?"收起高级设置":"高级设置";int height=expand?Math.Max(collapsedHeight+pathPanel.Height,pathPanel.Bottom+done.Height+48):collapsedHeight;ClientSize=new Size(ClientSize.Width,height);PerformLayout();}
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);if(done!=null)done.Location=new Point(ClientSize.Width-24-done.Width,ClientSize.Height-24-done.Height);}
}
}
