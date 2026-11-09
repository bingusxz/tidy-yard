using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using TidyYard.Services;

namespace TidyYard.Views;

/// <summary>Separate numeric time fields with an explicit AM/PM choice.</summary>
public sealed class TimeEntry : UserControl
{
    public static readonly DependencyProperty TimeTextProperty = DependencyProperty.Register(nameof(TimeText), typeof(string), typeof(TimeEntry),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));
    public static readonly DependencyProperty Uses24HourProperty = DependencyProperty.Register(nameof(Uses24Hour), typeof(bool), typeof(TimeEntry),
        new PropertyMetadata(false, OnValueChanged));
    public string TimeText { get => (string)GetValue(TimeTextProperty); set => SetValue(TimeTextProperty, value); }
    public bool Uses24Hour { get => (bool)GetValue(Uses24HourProperty); set => SetValue(Uses24HourProperty, value); }
    private readonly TextBox _hours = new() { Width = 40, MinWidth = 40, Padding = new Thickness(2, 0, 2, 0), MaxLength = 2, Tag = "HH", TextAlignment = TextAlignment.Center };
    private readonly TextBox _minutes = new() { Width = 40, MinWidth = 40, Padding = new Thickness(2, 0, 2, 0), MaxLength = 2, Tag = "MM", TextAlignment = TextAlignment.Center };
    private readonly ComboBox _period = new() { Width = 76, Padding = new Thickness(6, 0, 6, 0), ItemsSource = new[] { "AM", "PM" }, SelectedIndex = 0, Margin = new Thickness(4, 0, 0, 0) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
    private bool _updating;

    public TimeEntry()
    {
        var root = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 40 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 40 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_hours);
        row.Children.Add(new TextBlock { Text = ":", Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_minutes);
        _period.SetResourceReference(StyleProperty, "ComboBox.Flat");
        row.Children.Add(_period);
        var clear = new Button { Content = "\u00D7", Width = 22, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), ToolTip = "Clear time" };
        clear.SetResourceReference(StyleProperty, "Button.Text");
        AutomationProperties.SetName(clear, "Clear time");
        clear.Click += (_, _) => { SetCurrentValue(TimeTextProperty, string.Empty); Refresh(); };
        row.Children.Add(clear);
        for (var i = 0; i < row.Children.Count; i++) Grid.SetColumn(row.Children[i], i);
        root.Children.Add(row);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        root.Children.Add(_error);
        Content = root;
        AutomationProperties.SetName(_hours, "Hour");
        AutomationProperties.SetName(_minutes, "Minute");
        AutomationProperties.SetName(_period, "AM or PM");
        foreach (var field in new[] { _hours, _minutes })
        {
            field.PreviewTextInput += (_, e) => e.Handled = e.Text.Any(c => c < '0' || c > '9');
            DataObject.AddPastingHandler(field, (_, e) =>
            {
                if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || text.Any(c => c < '0' || c > '9')) e.CancelCommand();
            });
            field.TextChanged += (_, _) => Publish();
        }
        _period.SelectionChanged += (_, _) => Publish();
        Refresh();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TimeEntry)d).Refresh();

    private void Refresh()
    {
        if (_updating) return;
        _updating = true;
        _period.Visibility = Uses24Hour ? Visibility.Collapsed : Visibility.Visible;
        if (TimeFormatService.TryParse(TimeText, out var time))
        {
            _hours.Text = (Uses24Hour ? time.Hour : (time.Hour + 11) % 12 + 1).ToString("00", CultureInfo.InvariantCulture);
            _minutes.Text = time.Minute.ToString("00", CultureInfo.InvariantCulture);
            _period.SelectedIndex = time.Hour >= 12 ? 1 : 0;
            _error.Visibility = Visibility.Collapsed;
        }
        else if (string.IsNullOrWhiteSpace(TimeText))
        {
            _hours.Clear(); _minutes.Clear(); _period.SelectedIndex = 0;
            _error.Visibility = Visibility.Collapsed;
        }
        _updating = false;
    }

    private void Publish()
    {
        if (_updating) return;
        string value;
        bool valid = true;
        if (_hours.Text.Length == 0 && _minutes.Text.Length == 0) value = string.Empty;
        else if (int.TryParse(_hours.Text, out var h) && h >= (Uses24Hour ? 0 : 1) && h <= (Uses24Hour ? 23 : 12)
            && int.TryParse(_minutes.Text.Length == 0 ? "0" : _minutes.Text, out var m) && m <= 59)
        {
            if (!Uses24Hour) h = h % 12 + (_period.SelectedIndex == 1 ? 12 : 0);
            value = new TimeOnly(h, m).ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        else { value = "Invalid time"; valid = false; }
        _error.Text = Uses24Hour ? "Hour: 00?23. Minute: 00?59." : "Hour: 01?12. Minute: 00?59.";
        _error.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        _updating = true;
        SetCurrentValue(TimeTextProperty, value);
        _updating = false;
    }
}
