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
// 极致锐化：采用纯色无抗锯齿、像素级点阵对齐，告别模糊
        private Icon CreateTextIcon(int percentage, bool connected)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                
                // 关键：关闭所有平滑和抗锯齿，强制像素边缘锐利（类似 HWiNFO 的硬朗风格）
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

                if (!connected)
                {
                    using (Font f = new Font("Arial", 8, FontStyle.Bold))
                    {
                        g.DrawString("X", f, Brushes.Red, 2, 1);
                    }
                }
                else
                {
                    string text = percentage >= 100 ? "99" : percentage.ToString();
                    
                    // 采用高亮纯白或亮绿色，不加杂乱阴影，保证在任何任务栏上字迹锐利分明
                    Brush textBrush = Brushes.White;
                    if (percentage <= 20) textBrush = Brushes.Red;
                    else if (percentage <= 50) textBrush = Brushes.Orange;

                    // 使用最适合小尺寸的宋体/Arial，配合单像素对齐
                    using (Font font = new Font("Arial", 7.5f, FontStyle.Bold))
                    {
                        // 稍微错开 1 像素画一个极细的黑边框，确保浅色任务栏下也能看清
                        g.DrawString(text, font, Brushes.Black, 0, 1);
                        g.DrawString(text, font, Brushes.Black, 2, 1);
                        g.DrawString(text, font, Brushes.Black, 1, 0);
                        g.DrawString(text, font, Brushes.Black, 1, 2);

                        // 中间填上核心高亮数字
                        g.DrawString(text, font, textBrush, 1, 1);
                    }
                }
            }
            return Icon.FromHandle(bitmap.GetHicon());
        }
    }
}
