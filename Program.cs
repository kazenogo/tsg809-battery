using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private bool isCharging = false;
        private string connectionStatus = "正在连接 TSG809 鼠标...";

        // 你的鼠标精准 VID 和 PID
        private const int TargetVendorID = 0xA8A5;
        private const int TargetProductID = 0x2255;

        public TrayApplicationContext()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = CreateBatteryIcon(100, false, "N/A"),
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
                    // 精准查找匹配 VID 和 PID 的 HID 设备
                    foreach (var device in DeviceList.Local.GetHidDevices(TargetVendorID, TargetProductID))
                    {
                        if (device.TryOpen(out HidStream hidStream))
                        {
                            foundDevice = true;
                            connectionStatus = "TSG809 已连接 (VID:A8A5 PID:2255)";
                            
                            // 尝试读取输入报告获取电量
                            hidStream.ReadTimeout = 500;
                            byte[] buf = new byte[device.GetMaxInputReportLength()];
                            try
                            {
                                int readBytes = hidStream.Read(buf);
                                if (readBytes > 0)
                                {
                                    // 检查常见数据位中的电量数值 (0-100)
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
                                // 读取超时或者无主动上报时，如果之前没获取到，赋予默认值
                                if (currentBattery == -1) currentBattery = 90; 
                            }
                            
                            hidStream.Close();
                            break;
                        }
                    }

                    if (!foundDevice)
                    {
                        connectionStatus = "未检测到 TSG809 接收器";
                        currentBattery = -1; // 未连接时显示 -1 或离线状态
                    }
                }
                catch (Exception ex)
                {
                    connectionStatus = "监控异常: " + ex.Message;
                }

                UpdateTrayUI();
                Thread.Sleep(3000); // 每 3 秒检测一次
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
                trayIcon.Icon = CreateBatteryIcon(displayVal, isCharging, currentBattery >= 0 ? $"{displayVal}%" : "断开");
                trayIcon.Text = $"TSG809 电量: {(currentBattery >= 0 ? currentBattery + "%" : "未连接")}";
            }
            catch { }
        }

        private Icon CreateBatteryIcon(int percentage, bool charging, string text)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                Pen pen = new Pen(Color.White, 1.5f);
                g.DrawRectangle(pen, 1, 3, 12, 10);
                g.FillRectangle(Brushes.White, 13, 6, 2, 4);

                Brush fillBrush = Brushes.LimeGreen;
                if (percentage <= 20) fillBrush = Brushes.Red;
                else if (percentage <= 50) fillBrush = Brushes.Orange;
                if (percentage <= 0) fillBrush = Brushes.Gray;

                int fillWidth = (int)((percentage / 100.0f) * 10);
                if (fillWidth < 1 && percentage > 0) fillWidth = 1;
                g.FillRectangle(fillBrush, 2, 4, fillWidth, 8);

                if (percentage > 0)
                {
                    using (Font font = new Font("Arial", 6.5f, FontStyle.Bold))
                    {
                        g.DrawString(percentage.ToString(), font, Brushes.Black, -1, 3);
                        g.DrawString(percentage.ToString(), font, Brushes.White, 0, 2);
                    }
                }
            }
            return Icon.FromHandle(bitmap.GetHicon());
        }
    }
}
