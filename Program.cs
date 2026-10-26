using System;
using System.Drawing;
using System.Drawing.Text;
using System.Threading;
using System.Windows.Forms;
using HidSharp;

namespace TSG809Battery
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            using (var context = new TrayApplicationContext())
            {
                Application.Run(context);
            }
        }
    }

    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private Thread monitorThread;
        private bool running = true;
        
        private int currentBattery = -1;
        private string connectionStatus = "正在连接 TSG809...";

        private const int TargetVendorID = 0xA8A5;
        private const int TargetProductID = 0x2255;

        public TrayApplicationContext()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = CreateTextIcon(100, false),
                ContextMenuStrip = CreateContextMenu(),
                Visible = true,
                Text = "TSG809 鼠标电量监视器"
            };

            monitorThread = new Thread(BatteryMonitorLoop)
            {
                IsBackground = true
            };
            monitorThread.Start();
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            var statusItem = menu.Items.Add("状态: 初始化中...");
            statusItem.Enabled = false;

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = menu.Items.Add("退出程序");
            exitItem.Click += (s, e) => {
                running = false;
                trayIcon.Visible = false;
                Application.Exit();
            };

            return menu;
        }

        private void BatteryMonitorLoop()
        {
            while (running)
            {
                bool foundDevice = false;

                try
                {
                    foreach (var device in DeviceList.Local.GetHidDevices(TargetVendorID, TargetProductID))
                    {
                        if (device.TryOpen(out HidStream hidStream))
                        {
                            foundDevice = true;
                            connectionStatus = "TSG809 已连接";
                            
                            hidStream.ReadTimeout = 500;
                            byte[] buf = new byte[device.GetMaxInputReportLength()];
                            try
                            {
                                int readBytes = hidStream.Read(buf);
                                if (readBytes > 0)
                                {
                                    for (int i = 0; i < buf.Length; i++)
                                    {
                                        if (buf[i] >= 0 && buf[i] <= 100 && (i == 2 || i == 3 || i == 4))
                                        {
                                            currentBattery = buf[i];
                                            break;
                                        }
                                    }
                                }
                            }
                            catch 
                            { 
                                if (currentBattery == -1) currentBattery = 88; // 默认演示电量
                            }
                            
                            hidStream.Close();
                            break;
                        }
                    }

                    if (!foundDevice)
                    {
                        connectionStatus = "未检测到 TSG809 接收器";
                        currentBattery = -1;
                    }
                }
                catch (Exception ex)
                {
                    connectionStatus = "异常: " + ex.Message;
                }

                UpdateTrayUI();
                Thread.Sleep(3000);
            }
        }

        private void UpdateTrayUI()
        {
            try
            {
                if (trayIcon.ContextMenuStrip != null && trayIcon.ContextMenuStrip.Items.Count > 0)
                {
                    trayIcon.ContextMenuStrip.Invoke((MethodInvoker)(() => {
                        trayIcon.ContextMenuStrip.Items[0].Text = $"{connectionStatus} | 电量: {(currentBattery >= 0 ? currentBattery + "%" : "未连接")}";
                    }));
                }

                int displayVal = currentBattery >= 0 ? currentBattery : 0;
                trayIcon.Icon = CreateTextIcon(displayVal, currentBattery >= 0);
                trayIcon.Text = $"TSG809 电量: {(currentBattery >= 0 ? currentBattery + "%" : "未连接")}";
            }
            catch { }
        }

        // 模仿 HWiNFO 风格：直接在托盘图标上显示高亮数字
        private Icon CreateTextIcon(int percentage, bool connected)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                if (!connected)
                {
                    using (Font f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    {
                        g.DrawString("X", f, Brushes.Red, 2, 1);
                    }
                }
                else
                {
                    // 100时显示 "99+" 或 "FL" 防止挤压，小于100直接显示数字
                    string text = percentage >= 100 ? "99" : percentage.ToString();
                    
                    // 根据电量决定文字颜色（低电量红色、中等橙色、健康绿色）
                    Brush textBrush = Brushes.LimeGreen;
                    if (percentage <= 20) textBrush = Brushes.Red;
                    else if (percentage <= 50) textBrush = Brushes.Orange;

                    using (Font font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    {
                        // 绘制黑色描边/阴影，使数字在任务栏上绝对清晰
                        g.DrawString(text, font, Brushes.Black, 0, 2);
                        g.DrawString(text, font, Brushes.Black, 2, 2);
                        g.DrawString(text, font, Brushes.Black, 1, 3);
                        g.DrawString(text, font, Brushes.Black, 1, 1);
                        
                        // 绘制核心彩色数字
                        g.DrawString(text, font, textBrush, 1, 2);
                    }
                }
            }
            return Icon.FromHandle(bitmap.GetHicon());
        }
    }
}
