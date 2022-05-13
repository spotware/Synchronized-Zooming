using cAlgo.API;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.Threading;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SynchronizedZooming : Indicator
    {
        private static ConcurrentDictionary<string, IndicatorInstanceContainer<SynchronizedZooming, int?>> _indicatorInstances = new ConcurrentDictionary<string, IndicatorInstanceContainer<SynchronizedZooming, int?>>();

        private static int _numberOfChartsToScroll;

        private int _lastZoomLevel;

        private string _chartKey;

        [Parameter("Mode", DefaultValue = Mode.All)]
        public Mode Mode { get; set; }

        protected override void Initialize()
        {
            _chartKey = GetChartKey(this);

            IndicatorInstanceContainer<SynchronizedZooming, int?> oldIndicatorContainer;

            GetIndicatorInstanceContainer(_chartKey, out oldIndicatorContainer);

            _indicatorInstances.AddOrUpdate(_chartKey, new IndicatorInstanceContainer<SynchronizedZooming, int?>(this), (key, value) => new IndicatorInstanceContainer<SynchronizedZooming, int?>(this));

            if (oldIndicatorContainer != null && oldIndicatorContainer.Data.HasValue)
            {
                ZoomChart(oldIndicatorContainer.Data.Value);
            }

            Chart.ZoomChanged += Chart_ZoomChanged;
        }

        public override void Calculate(int index)
        {
        }

        private void Chart_ZoomChanged(ChartZoomEventArgs obj)
        {
            IndicatorInstanceContainer<SynchronizedZooming, int?> indicatorContainer;

            if (GetIndicatorInstanceContainer(_chartKey, out indicatorContainer))
            {
                indicatorContainer.Data = null;
            }

            if (_numberOfChartsToScroll > 0)
            {
                Interlocked.Decrement(ref _numberOfChartsToScroll);

                return;
            }

            var zoomLevel = obj.Chart.ZoomLevel;

            if (_lastZoomLevel == zoomLevel) return;

            _lastZoomLevel = zoomLevel;

            switch (Mode)
            {
                case Mode.Symbol:
                    ZoomCharts(zoomLevel, indicator => indicator.SymbolName.Equals(SymbolName, StringComparison.Ordinal));
                    break;

                case Mode.TimeFrame:
                    ZoomCharts(zoomLevel, indicator => indicator.TimeFrame == TimeFrame);
                    break;

                default:

                    ZoomCharts(zoomLevel);
                    break;
            }
        }

        public void ZoomChart(int zoomLevel)
        {
            IndicatorInstanceContainer<SynchronizedZooming, int?> indicatorContainer;

            if (GetIndicatorInstanceContainer(_chartKey, out indicatorContainer))
            {
                indicatorContainer.Data = zoomLevel;
            }

            Chart.ZoomLevel = zoomLevel;
        }

        private void ZoomCharts(int zoomLevel, Func<Indicator, bool> predicate = null)
        {
            var toScroll = new List<SynchronizedZooming>(_indicatorInstances.Values.Count);

            foreach (var indicatorContianer in _indicatorInstances)
            {
                SynchronizedZooming indicator;

                if (indicatorContianer.Value.GetIndicator(out indicator) == false || indicator == this || (predicate != null && predicate(indicator) == false))
                    continue;

                toScroll.Add(indicator);
            }

            Interlocked.CompareExchange(ref _numberOfChartsToScroll, toScroll.Count, _numberOfChartsToScroll);

            foreach (var indicator in toScroll)
            {
                try
                {
                    indicator.BeginInvokeOnMainThread(() => indicator.ZoomChart(zoomLevel));
                }
                catch (Exception)
                {
                    Interlocked.Decrement(ref _numberOfChartsToScroll);
                }
            }
        }

        private string GetChartKey(SynchronizedZooming indicator)
        {
            return string.Format("{0}_{1}_{2}", indicator.SymbolName, indicator.TimeFrame, indicator.Chart.ChartType);
        }

        private bool GetIndicatorInstanceContainer(string chartKey, out IndicatorInstanceContainer<SynchronizedZooming, int?> indicatorContainer)
        {
            if (_indicatorInstances.TryGetValue(chartKey, out indicatorContainer))
            {
                return true;
            }

            indicatorContainer = null;

            return false;
        }
    }

    public enum Mode
    {
        All,
        TimeFrame,
        Symbol
    }

    public class IndicatorInstanceContainer<T, TData> where T : Indicator
    {
        private readonly WeakReference _indicatorWeakReference;

        public IndicatorInstanceContainer(T indicator)
        {
            _indicatorWeakReference = new WeakReference(indicator);
        }

        public TData Data { get; set; }

        public bool GetIndicator(out T indicator)
        {
            if (_indicatorWeakReference.IsAlive)
            {
                indicator = (T)_indicatorWeakReference.Target;

                return true;
            }

            indicator = null;

            return false;
        }
    }
}