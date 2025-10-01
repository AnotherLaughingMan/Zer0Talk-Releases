using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;

namespace ZTalk.Controls
{
    public partial class Spinner : UserControl
    {
        private DispatcherTimer? _timer;
        private double _angle;

        public static readonly StyledProperty<bool> IsActiveProperty =
            AvaloniaProperty.Register<Spinner, bool>(nameof(IsActive));

        public static readonly StyledProperty<double> AngleProperty =
            AvaloniaProperty.Register<Spinner, double>(nameof(Angle));

        public Spinner()
        {
            InitializeComponent();
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, OnTick);
            this.GetObservable(IsActiveProperty).Subscribe(new ActionObserver<bool>(active =>
            {
                if (active) Start(); else Stop();
            }));
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _angle = (_angle + 30) % 360;
            Angle = _angle;
        }

        public bool IsActive
        {
            get => GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public double Angle
        {
            get => GetValue(AngleProperty);
            set => SetValue(AngleProperty, value);
        }

        private void Start()
        {
            _angle = 0;
            _timer?.Start();
        }

        private void Stop()
        {
            _timer?.Stop();
            _angle = 0;
            Angle = 0;
        }

        private sealed class ActionObserver<T> : IObserver<T>
        {
            private readonly Action<T> _onNext;
            public ActionObserver(Action<T> onNext) => _onNext = onNext;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(T value) => _onNext(value);
        }
    }
}
