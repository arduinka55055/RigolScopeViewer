using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RigolScopeViewer
{
    public enum IncrementType
    {
        Logarithmic,  // 1-2-5 stepping
        Step          // Linear stepping
    }

    public partial class UnitNumericUpDown : UserControl
    {
        // Static list of SI prefixes
        private static readonly List<UnitPrefix> Prefixes = new()
        {
            new UnitPrefix("p", 1e-12),
            new UnitPrefix("n", 1e-9),
            new UnitPrefix("μ", 1e-6),
            new UnitPrefix("m", 1e-3),
            new UnitPrefix("", 1),
            new UnitPrefix("k", 1e3),
            new UnitPrefix("M", 1e6),
            new UnitPrefix("G", 1e9),
            new UnitPrefix("T", 1e12)
        };

        // Avalonia properties
        public static readonly StyledProperty<double> BaseValueProperty =
            AvaloniaProperty.Register<UnitNumericUpDown, double>(
                nameof(BaseValue), defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<string> UnitSymbolProperty =
            AvaloniaProperty.Register<UnitNumericUpDown, string>(nameof(UnitSymbol), "V");

        public static readonly StyledProperty<IncrementType> IncrementTypeProperty =
            AvaloniaProperty.Register<UnitNumericUpDown, IncrementType>(nameof(IncrementType), IncrementType.Logarithmic);

        public static readonly StyledProperty<double> StepProperty =
            AvaloniaProperty.Register<UnitNumericUpDown, double>(nameof(Step), 1.0);

        private UnitPrefix _currentPrefix;
        private bool _updating;
        private bool _initialized;
        private bool _autoConverting;

        public double BaseValue
        {
            get => GetValue(BaseValueProperty);
            set => SetValue(BaseValueProperty, value);
        }

        public string UnitSymbol
        {
            get => GetValue(UnitSymbolProperty);
            set => SetValue(UnitSymbolProperty, value);
        }

        public IncrementType IncrementType
        {
            get => GetValue(IncrementTypeProperty);
            set => SetValue(IncrementTypeProperty, value);
        }

        public double Step
        {
            get => GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        public UnitNumericUpDown()
        {
            InitializeComponent();
            _currentPrefix = Prefixes[4]; // Default to no prefix (index 4)
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_initialized)
            {
                InitializeControl();
                _initialized = true;
            }
        }

        private void InitializeControl()
        {
            UnitComboBox.ItemsSource = Prefixes
                .Select(p => $"{p.Prefix}{UnitSymbol}")
                .ToList();

            UnitComboBox.SelectedIndex = Prefixes.IndexOf(_currentPrefix);
            UpdateDisplayValue();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BaseValueProperty && _initialized)
            {
                UpdateDisplayValue();
            }
            else if (change.Property == UnitSymbolProperty && _initialized)
            {
                UpdateUnitComboBoxItems();
            }
        }

        private void UpdateUnitComboBoxItems()
        {
            var selectedIndex = UnitComboBox.SelectedIndex;
            UnitComboBox.ItemsSource = Prefixes
                .Select(p => $"{p.Prefix}{UnitSymbol}")
                .ToList();

            UnitComboBox.SelectedIndex = selectedIndex;
        }

        private void UpdateDisplayValue()
        {
            if (_updating || _autoConverting) return;

            NumericBox.Value = (decimal)(BaseValue / _currentPrefix.Multiplier);
        }

        private void UnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating || UnitComboBox.SelectedIndex < 0) return;

            var newPrefix = Prefixes[UnitComboBox.SelectedIndex];
            if (newPrefix == _currentPrefix) return;

            _updating = true;
            _currentPrefix = newPrefix;
            BaseValue = (double)(NumericBox.Value ?? 0) * _currentPrefix.Multiplier;
            _updating = false;
        }

        private void NumericBox_ValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_updating || _autoConverting) return;

            _updating = true;
            BaseValue = (double)(e.NewValue ?? 0) * _currentPrefix.Multiplier;
            _updating = false;

            AutoConvertToAppropriatePrefix();
        }

        private void NumericBox_LostFocus(object sender, RoutedEventArgs e)
        {
            AutoConvertToAppropriatePrefix();
        }

        private void AutoConvertToAppropriatePrefix()
        {
            if (_updating || _autoConverting) return;

            _autoConverting = true;
            try
            {
                var currentDisplayValue = (double)(NumericBox.Value ?? 0);
                var baseValue = currentDisplayValue * _currentPrefix.Multiplier;
                var absBaseValue = Math.Abs(baseValue);

                if (absBaseValue == 0)
                {
                    _autoConverting = false;
                    return;
                }

                // Find the best prefix for the current base value
                var bestPrefix = _currentPrefix;
                var changed = false;

                // Find the prefix where the normalized value is between 1 and 1000
                foreach (var prefix in Prefixes.OrderByDescending(p => p.Multiplier))
                {
                    var normalizedValue = absBaseValue / prefix.Multiplier;

                    if (normalizedValue >= 1 && normalizedValue < 1000)
                    {
                        if (prefix != _currentPrefix)
                        {
                            bestPrefix = prefix;
                            changed = true;
                        }
                        break;
                    }
                }

                if (changed)
                {
                    _updating = true;
                    _currentPrefix = bestPrefix;
                    UnitComboBox.SelectedIndex = Prefixes.IndexOf(bestPrefix);

                    // Calculate new display value: baseValue / newMultiplier
                    var newDisplayValue = baseValue / bestPrefix.Multiplier;
                    NumericBox.Value = (decimal)newDisplayValue;

                    // BaseValue remains the same (baseValue doesn't change)
                    _updating = false;
                }
            }
            finally
            {
                _autoConverting = false;
            }
        }

        private void UnitComboBox_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
            UnitComboBox.Focus();
        }

        private void StepUpButton_Click(object sender, RoutedEventArgs e) => StepValue(true);
        private void StepDownButton_Click(object sender, RoutedEventArgs e) => StepValue(false);

        private void StepValue(bool up)
        {
            var currentValue = (double)(NumericBox.Value ?? 0);
            double newValue = 0;

            if (IncrementType == IncrementType.Step)
            {
                //todo: this idea sucks but works
                newValue = StepLinear(currentValue * _currentPrefix.Multiplier, up) / _currentPrefix.Multiplier;

            }
            else // Logarithmic
            {
                newValue = StepLogarithmic(currentValue, up);
            }

            _updating = true;
            NumericBox.Value = (decimal)newValue;
            BaseValue = newValue * _currentPrefix.Multiplier;
            _updating = false;

            AutoConvertToAppropriatePrefix();
        }

        private double StepLinear(double currentValue, bool up)
        {
            var step = Step / 10;
            if (step <= 0) step = 1; // Ensure positive step

            var newValue = currentValue;

            if (up)
            {
                newValue += step;
            }
            else
            {
                newValue -= step;
            }

            // Handle crossing zero for negative values
            if (currentValue < 0 && up && newValue >= 0)
            {
                newValue = 0;
            }
            else if (currentValue > 0 && !up && newValue <= 0)
            {
                newValue = 0;
            }

            return newValue;
        }

        private double StepLogarithmic(double value, bool up)
        {
            if (value == 0) return up ? 1 : -1;

            var negative = value < 0;
            var absValue = Math.Abs(value);

            // Handle very small values
            if (absValue < 1e-12) return up ? 1e-12 : -1e-12;

            var exponent = (int)Math.Floor(Math.Log10(absValue));
            var stepFactor = Math.Pow(10, exponent);

            double[] steps = { 1 * stepFactor, 2 * stepFactor, 5 * stepFactor };
            var nextValue = absValue;

            if (up)
            {
                if (absValue < steps[0]) nextValue = steps[0];
                else if (absValue < steps[1]) nextValue = steps[1];
                else if (absValue < steps[2]) nextValue = steps[2];
                else nextValue = 10 * stepFactor;
            }
            else
            {
                if (absValue > steps[2]) nextValue = steps[2];
                else if (absValue > steps[1]) nextValue = steps[1];
                else if (absValue > steps[0]) nextValue = steps[0];
                else
                {
                    // Handle wrap-around to smaller magnitude
                    var smallerFactor = Math.Pow(10, exponent - 1);
                    nextValue = 5 * smallerFactor;

                    // Ensure we don't go below minimum
                    if (nextValue < 1e-12) nextValue = 1e-12;
                }
            }

            // Handle negative values
            if (negative)
            {
                nextValue = -nextValue;

                // Special case: crossing zero
                if (up && nextValue >= 0)
                {
                    nextValue = 0;
                }
                else if (!up && nextValue > 0)
                {
                    nextValue = -nextValue;
                }
            }
            else
            {
                // Special case: crossing zero
                if (!up && nextValue <= 0)
                {
                    nextValue = 0;
                }
                else if (up && nextValue < 0)
                {
                    nextValue = -nextValue;
                }
            }

            return nextValue;
        }
    }

    public class UnitPrefix(string prefix, double multiplier)
    {
        public string Prefix { get; } = prefix;
        public double Multiplier { get; } = multiplier;
    }
}
