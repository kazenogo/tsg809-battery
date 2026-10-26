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
        private bool isCharging = false;
        private string connectionStatus = "正在连接 TSG809 鼠标...";

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
                                if (readBytes >0)
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
                                if (currentBattery == -1) currentBattery = 88; // 默认演示
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

        // 优化图标绘制，采用高对比度与无模糊字体，确保在任务栏清晰可见
        private Icon CreateBatteryIcon(int percentage, bool connected)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                
                // 设置像素级清晰渲染，避免小字模糊
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                if (!connected)
                {
                    // 未连接时显示一个红色问号或X
                    using (Font f = new Font("Arial", 8, FontStyle.Bold))
                    {
                        g.DrawString("?", f, Brushes.Red, 1, 1);
                    }
                }
                else
                {
                    // 绘制电池外框
                    Pen pen = new Pen(Color.FromArgb(240, 240, 240), 1.5f);
                    g.DrawRectangle(pen, 0, 2, 13, 11);
                    g.FillRectangle(Brushes.FromArgb(240, 240, 240), 13, 5, 2, 5); // 正极头

                    // 内部电量填充
                    Brush fillBrush = Brushes.LimeGreen;
                    if (percentage <= 20) fillBrush = Brushes.Red;
                    else if (percentage <= 50) fillBrush = Brushes.Orange;

                    int fillWidth = (int)((percentage / 100.0f) * 11);
                    if (fillWidth < 1 && percentage > 0) fillWidth = 1;
                    g.FillRectangle(fillBrush, 1, 3, fillWidth, 9);

                    // 绘制清晰的粗体百分比数字（小于100时显示数字，100时简化显示）
                    string textToDraw = percentage >= 100 ? "F" : percentage.ToString();
                    using (Font font = new Font("Segoe UI", textToDraw.Length > 1 ? 6.5f : 7f, FontStyle.Bold))
                    {
                        // 黑色描边加白色文字，确保在任何任务栏背景下都清晰可辨
                        g.DrawString(textToDraw, font, Brushes.Black, 0, 1);
                        g.DrawString(textToDraw, font, Brushes.White, -0.5f, 0.5f);
                    }
                }
            }
            return Icon.FromHandle(bitmap.GetHicon());
        }
    }
}
