using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RigolScopeViewer
{
    public partial class UnitNumericUpDown : UserControl
    {
        // Static list of SI prefixes
        private static readonly List<UnitPrefix> Prefixes = new()
        {
            new UnitPrefix("p", 1e-12),
            new UnitPrefix("μ", 1e-6),
            new UnitPrefix("m", 1e-3),
            new UnitPrefix("", 1),
            new UnitPrefix("k", 1e3),
            new UnitPrefix("M", 1e6),
            new UnitPrefix("G", 1e9)
        };

        // Avalonia properties
        public static readonly StyledProperty<double> BaseValueProperty =
            AvaloniaProperty.Register<UnitNumericUpDown, double>(
                nameof(BaseValue), defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<string> UnitSymbolProperty =
            AvaloniaProperty.Register<UnitNumericUpDown, string>(nameof(UnitSymbol), "V");

        private UnitPrefix _currentPrefix;
        private bool _updating;
        private bool _initialized;

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

        public UnitNumericUpDown()
        {
            InitializeComponent();
            _currentPrefix = Prefixes[3]; // Default to no prefix
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
            // Use ItemsSource
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
            if (_updating) return;

            // Convert double to decimal for NumericUpDown
            NumericBox.Value = (decimal)(BaseValue / _currentPrefix.Multiplier);
        }

        private void UnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating || UnitComboBox.SelectedIndex < 0) return;

            var newPrefix = Prefixes[UnitComboBox.SelectedIndex];
            if (newPrefix == _currentPrefix) return;

            _updating = true;
            _currentPrefix = newPrefix;

            // Convert decimal to double
            BaseValue = (double)(NumericBox.Value ?? 0) * _currentPrefix.Multiplier;
            _updating = false;
        }

        private void NumericBox_ValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_updating) return;

            _updating = true;
            // Convert decimal to double
            BaseValue = (double)(e.NewValue ?? 0) * _currentPrefix.Multiplier;

            // Auto-switch units if needed
            AutoConvertToAppropriatePrefix();
            _updating = false;
        }

        private void NumericBox_LostFocus(object sender, RoutedEventArgs e)
        {
            AutoConvertToAppropriatePrefix();
        }

        private void AutoConvertToAppropriatePrefix()
        {
            if (_updating) return;

            double currentValue = (double)(NumericBox.Value ?? 0);
            double absValue = Math.Abs(currentValue);

            if (absValue == 0) return;

            UnitPrefix newPrefix = _currentPrefix;

            // Auto-switch to appropriate prefix
            if (absValue >= 1000)
            {
                // Find next higher prefix
                var currentIndex = Prefixes.IndexOf(_currentPrefix);
                for (int i = currentIndex + 1; i < Prefixes.Count; i++)
                {
                    var testPrefix = Prefixes[i];
                    double testValue = absValue * (_currentPrefix.Multiplier / testPrefix.Multiplier);
                    if (testValue < 1000)
                    {
                        newPrefix = testPrefix;
                        break;
                    }
                }
            }
            else if (absValue < 0.001)
            {
                // Find next lower prefix
                var currentIndex = Prefixes.IndexOf(_currentPrefix);
                for (int i = currentIndex - 1; i >= 0; i--)
                {
                    var testPrefix = Prefixes[i];
                    double testValue = absValue * (_currentPrefix.Multiplier / testPrefix.Multiplier);
                    if (testValue >= 0.001)
                    {
                        newPrefix = testPrefix;
                        break;
                    }
                }
            }

            if (newPrefix != _currentPrefix)
            {
                _updating = true;
                _currentPrefix = newPrefix;
                UnitComboBox.SelectedIndex = Prefixes.IndexOf(newPrefix);
                BaseValue = currentValue * _currentPrefix.Multiplier;
                _updating = false;
            }
        }

        // Fix focus issue when clicking on ComboBox
        private void UnitComboBox_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
        }

        private void StepUpButton_Click(object sender, RoutedEventArgs e) => StepValue(true);
        private void StepDownButton_Click(object sender, RoutedEventArgs e) => StepValue(false);

        private void StepValue(bool up)
        {
            double currentValue = (double)(NumericBox.Value ?? 0);
            double newValue = GetNextStepValue(currentValue, up);

            _updating = true;
            NumericBox.Value = (decimal)newValue;
            BaseValue = newValue * _currentPrefix.Multiplier;
            _updating = false;
        }

        private double GetNextStepValue(double value, bool up)
        {
            if (value == 0) return up ? 1 : -1;

            bool negative = value < 0;
            double absValue = Math.Abs(value);
            int exponent = (int)Math.Floor(Math.Log10(absValue));
            double stepFactor = Math.Pow(10, exponent);

            double[] steps = { 1 * stepFactor, 2 * stepFactor, 5 * stepFactor };
            double nextValue = absValue;

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
                else nextValue = 5 * Math.Pow(10, exponent - 1);
            }

            return negative ? -nextValue : nextValue;
        }
    }

    public class UnitPrefix
    {
        public string Prefix { get; }
        public double Multiplier { get; }

        public UnitPrefix(string prefix, double multiplier)
        {
            Prefix = prefix;
            Multiplier = multiplier;
        }
    }
}