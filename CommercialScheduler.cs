using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace quasar_radio
{
    public class CommercialScheduler
    {
        private readonly DispatcherTimer _timer;
        private int _lastTriggeredMinute = -1;
        private int _lastTriggeredHour = -1;

        public ObservableCollection<CommercialBlock> Blocks { get; } = new ObservableCollection<CommercialBlock>();

        public event EventHandler<CommercialBlock>? BlockTriggered;

        public CommercialScheduler()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;

            // Evitar disparos duplicados en el mismo minuto y hora
            if (now.Minute == _lastTriggeredMinute && now.Hour == _lastTriggeredHour)
            {
                return;
            }

            foreach (var block in Blocks)
            {
                if (block.IsEnabled && block.Spots.Count > 0 && now.Minute == block.TriggerMinute && now.Second == 0)
                {
                    _lastTriggeredMinute = now.Minute;
                    _lastTriggeredHour = now.Hour;

                    BlockTriggered?.Invoke(this, block);
                    break;
                }
            }
        }

        public TimeSpan GetTimeUntilNextBlock()
        {
            DateTime now = DateTime.Now;
            var activeBlocks = Blocks.Where(b => b.IsEnabled && b.Spots.Count > 0).ToList();
            if (activeBlocks.Count == 0) return TimeSpan.Zero;

            int currentMinute = now.Minute;
            int currentSecond = now.Second;

            int nextMinute = -1;
            foreach (var block in activeBlocks.OrderBy(b => b.TriggerMinute))
            {
                if (block.TriggerMinute > currentMinute || (block.TriggerMinute == currentMinute && currentSecond == 0))
                {
                    nextMinute = block.TriggerMinute;
                    break;
                }
            }

            if (nextMinute == -1)
            {
                // La próxima tanda es en la siguiente hora
                nextMinute = activeBlocks.Min(b => b.TriggerMinute) + 60;
            }

            int diffSeconds = (nextMinute * 60) - (currentMinute * 60 + currentSecond);
            return TimeSpan.FromSeconds(Math.Max(0, diffSeconds));
        }
    }
}
