using System.Windows.Controls;

namespace FluentFlyoutWPF.Classes.Utils;

public static class MonitorUtil
{
    public static void UpdateMonitorList(
    ComboBox comboBox,
    Func<int> getSelectedIndex,
    Action<int> setSelectedIndex)
    {
        var monitors = WindowHelper.GetMonitors();
        comboBox.Items.Clear();

        int savedIndex = getSelectedIndex();

        bool resetToPrimary =
            savedIndex >= monitors.Count ||
            savedIndex < 0;

        int selectedMonitor = savedIndex;

        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];

            var cb = new ComboBoxItem
            {
                Content = monitor.isPrimary
                    ? $"{i + 1} *"
                    : (i + 1).ToString()
            };

            if (resetToPrimary && monitor.isPrimary)
                selectedMonitor = i;

            comboBox.Items.Add(cb);
        }

        comboBox.SelectedIndex = selectedMonitor;
        setSelectedIndex(selectedMonitor);
    }
}
