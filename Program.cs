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
            Application.Run(new TrayApplicationContext());
        }
    }

    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private Thread monitorThread;
        private bool running = true;
        
        // 模拟/实际状态
        private int currentBattery = -1;
        private bool isCharging = false;
        private string connectionStatus = "正在搜索 TSG809 鼠标...";

        public TrayApplicationContext()
        {
            // 初始化系统托盘图标
            trayIcon = new NotifyIcon()
            {
                Icon = CreateBatteryIcon(100, false, "N/A"),
                ContextMenuStrip = CreateContextMenu(),
                Visible = true,
                Text = "TSG809 鼠标电量监视器"
            };

            // 启动后台线程监听鼠标设备
            monitorThread = new Thread(BatteryMonitorLoop)
            {
                IsBackground = true
            };
            monitorThread.Start();
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            
            var statusItem = menu.Items.Add("状态: 正在初始化...");
            statusItem.Enabled = false;

            menu.Items.Add(new ToolStripSeparator());

            var refreshItem = menu.Items.Add("立即重新连接/刷新");
            refreshItem.Click += (s, e) => {
                // 触发重新检测
            };

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = menu.Items.Add("退出");
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
                try
                {
                    bool found = false;
                    // 遍历 HID 设备查找 TSG809 或常见无线接收器 VID/PID
                    // 注：若有特定的 VendorID / ProductID 可在此处精确匹配，或轮询所有HID设备
                    foreach (var device in DeviceList.Local.GetHidDevices())
                    {
                        string deviceName = "";
                        try { deviceName = device.GetProductName() ?? ""; } catch { }

                        // 兼容检查（可根据实际 TSG809 在设备管理器中看到的名称调整）
                        if (deviceName.Contains("TSG809", StringComparison.OrdinalIgnoreCase) || 
                            deviceName.Contains("Wireless Receiver", StringComparison.OrdinalIgnoreCase) ||
                            deviceName.Contains("USB Mouse", StringComparison.OrdinalIgnoreCase))
                        {
                            if (device.TryOpen(out HidStream hidStream))
                            {
                                found = true;
                                connectionStatus = $"已连接: {deviceName}";
                                
                                // 发送特征获取指令或尝试读取输入报告 (Input Report)
                                byte[] readBuffer = new byte[device.GetMaxInputReportLength()];
                                
                                // 设置超时
                                hidStream.ReadTimeout = 1000;
                                try
                                {
                                    int bytesRead = hidStream.Read(readBuffer);
                                    if (bytesRead > 0)
                                    {
                                        // 解析常见无线鼠标电量协议位置（通常在特定 Byte，如 Index 2 或 3）
                                        // 这里以通用解析为例：假设读取到了电量和充电状态
                                        int battery = readBuffer[2]; // 示例协议位
                                        if (battery >= 0 && battery <= 100)
                                        {
                                            currentBattery = battery;
                                            isCharging = (readBuffer[3] & 0x01) == 1; // 示例充电位
                                        }
                                    }
                                }
                                catch (TimeoutException)
                                {
                                    // 读超时属于正常现象（无主动上报时）
                                }
                                hidStream.Close();
                                break;
                            }
                        }
                    }

                    if (!found)
                    {
                        connectionStatus = "未找到 TSG809 鼠标 (使用模拟演示电量)";
                        // 如果未插接收器，为了方便测试，可在此处赋一个模拟值或保持未连接
                        // currentBattery = -1; 
                    }
                }
                catch (Exception ex)
                {
                    connectionStatus = "错误: " + ex.Message;
                }

                // 更新托盘图标UI
                UpdateTrayUI();

                // 每 5 秒轮询一次
                Thread.Sleep(5000);
            }
        }

        private void UpdateTrayUI()
        {
            if (trayIcon.ContextMenuStrip != null && trayIcon.ContextMenuStrip.Items.Count > 0)
            {
                trayIcon.ContextMenuStrip.Invoke((MethodInvoker)(() => {
                    trayIcon.ContextMenuStrip.Items[0].Text = connectionStatus + (currentBattery >= 0 ? $" | 电量: {currentBattery}%" : "");
                }));
            }

            // 如果成功获取电量，动态画一个电量图标显示在任务栏
            int displayVal = currentBattery >= 0 ? currentBattery : 85; // 无设备时默认显示85%作演示
            trayIcon.Icon = CreateBatteryIcon(displayVal, isCharging, currentBattery >= 0 ? $"{displayVal}%" : "离线");
            trayIcon.Text = $"TSG809 鼠标电量: {(currentBattery >= 0 ? currentBattery + "%" : "未连接")}{(isCharging ? " (充电中)" : "")}";
        }

        // 动态生成带数字和电量条的托盘图标
        private Icon CreateBatteryIcon(int percentage, bool charging, string text)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // 绘制电池外框
                Pen pen = new Pen(Color.White, 1.5f);
                g.DrawRectangle(pen, 1, 3, 12, 10);
                g.FillRectangle(Brushes.White, 13, 6, 2, 4); // 电池正极头

                // 根据电量决定内部填充颜色
                Brush fillBrush = Brushes.LimeGreen;
                if (percentage <= 20) fillBrush = Brushes.Red;
                else if (percentage <= 50) fillBrush = Brushes.Orange;
                if (charging) fillBrush = Brushes.DeepSkyBlue;

                // 计算填充宽度
                int fillWidth = (int)((percentage / 100.0f) * 10);
                if (fillWidth < 1 && percentage > 0) fillWidth = 1;
                g.FillRectangle(fillBrush, 2, 4, fillWidth, 8);

                // 如果空间允许，叠加文字
                if (percentage < 100)
                {
                    using (Font font = new Font("Arial", 6.5f, FontStyle.Bold))
                    {
                        // 绘制数字阴影或直接绘制
                        g.DrawString(percentage.ToString(), font, Brushes.Black, -1, 3);
                        g.DrawString(percentage.ToString(), font, Brushes.White, 0, 2);
                    }
                }
            }
            return Icon.FromHandle(bitmap.GetHicon());
        }
    }
}