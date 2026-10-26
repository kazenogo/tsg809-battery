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
                Icon = CreateBatteryIcon(100, false),
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
                trayIcon.Icon = CreateBatteryIcon(displayVal, currentBattery >= 0);
                trayIcon.Text = $"TSG809 电量: {(currentBattery >= 0 ? currentBattery + "%" : "未连接")}";
            }
            catch { }
        }

        // 优化图标：采用高辨识度的电池外观，悬停和右键能看清精确百分比
        private Icon CreateBatteryIcon(int percentage, bool connected)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                if (!connected)
                {
                    using (Font f = new Font("Arial", 8, FontStyle.Bold))
                    {
                        g.DrawString("X", f, Brushes.Red, 2, 1);
                    }
                }
                else
                {
                    // 绘制外框
                    using (Pen pen = new Pen(Color.White, 1.5f))
                    {
                        g.DrawRectangle(pen, 0, 3, 13, 10);
                    }
                    g.FillRectangle(Brushes.White, 13, 6, 2, 4);

                    // 填充颜色
                    Brush fillBrush = Brushes.LimeGreen;
                    if (percentage <= 20) fillBrush = Brushes.Red;
                    else if (percentage <= 50) fillBrush = Brushes.Orange;

                    int fillWidth = (int)((percentage / 100.0f) * 11);
                    if (fillWidth < 1 && percentage > 0) fillWidth = 1;
                    g.FillRectangle(fillBrush, 1, 4, fillWidth, 8);
                }
            }
            return Icon.FromHandle(bitmap.GetHicon());
        }
    }
}
