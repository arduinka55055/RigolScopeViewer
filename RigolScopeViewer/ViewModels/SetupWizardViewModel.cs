using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Reflection;

namespace RigolScopeViewer.ViewModels;

public partial class InspectorPropertyViewModel : ViewModelBase
{
    private readonly object _target;
    private readonly PropertyInfo _property;

    public InspectorPropertyViewModel(object target, PropertyInfo property)
    {
        _target = target;
        _property = property;
    }

    public string Name => _property.Name;

    public object? Value
    {
        get => _property.GetValue(_target);
        set
        {
            if (!_property.CanWrite) return;

            try
            {
                object? converted = null;

                if (value == null)
                {
                    // For non-nullable value types, do nothing or assign a specific default
                    if (_property.PropertyType.IsValueType && Nullable.GetUnderlyingType(_property.PropertyType) == null)
                        return; // or set to 0, but better to ignore
                }
                else if (_property.PropertyType.IsEnum)
                {
                    converted = Enum.Parse(_property.PropertyType, value.ToString()!);
                }
                else if (_property.PropertyType == typeof(bool) && value is bool b)
                {
                    converted = b;
                }
                else if (_property.PropertyType == typeof(int))
                {
                    if (int.TryParse(value.ToString(), out int intVal))
                        converted = intVal;
                    else
                        return; // ignore invalid input
                }
                else if (_property.PropertyType == typeof(double))
                {
                    if (double.TryParse(value.ToString(), out double dblVal))
                        converted = dblVal;
                    else
                        return;
                }
                // Add other numeric types as needed
                else if (value != null)
                {
                    converted = Convert.ChangeType(value, _property.PropertyType);
                }

                if (converted != null || !_property.PropertyType.IsValueType)
                {
                    _property.SetValue(_target, converted);
                    OnPropertyChanged(nameof(Value));
                }
            }
            catch
            {
                // Log if necessary, but ignore conversion failures
            }
        }
    }

    public Type PropertyType => _property.PropertyType;
    public bool IsEnum => _property.PropertyType.IsEnum;
    public bool IsBool => _property.PropertyType == typeof(bool);
    public bool IsNumber => _property.PropertyType == typeof(int) || _property.PropertyType == typeof(float) || _property.PropertyType == typeof(double) || _property.PropertyType == typeof(long) || _property.PropertyType == typeof(short) || _property.PropertyType == typeof(byte);
    public bool IsString => _property.PropertyType == typeof(string);
    public Array? EnumValues => IsEnum ? Enum.GetValues(_property.PropertyType) : null;
}

public partial class SetupWizardViewModel : ViewModelBase
{
    [ObservableProperty]
    private object? _configObject;

    partial void OnConfigObjectChanged(object? value)
    {
        GenerateProperties();
    }

    [ObservableProperty]
    private string _currentFilePath = string.Empty;

    [ObservableProperty]
    private object? _previewContent;

    public ObservableCollection<InspectorPropertyViewModel> Properties { get; } = new();

    public SetupWizardViewModel()
    {
    }

    private void GenerateProperties()
    {
        Properties.Clear();
        if (ConfigObject == null) return;

        var props = ConfigObject.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in props)
        {
            if (prop.CanRead && prop.CanWrite)
            {
                Properties.Add(new InspectorPropertyViewModel(ConfigObject, prop));
            }
        }
    }
}
