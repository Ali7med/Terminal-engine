using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TerminalLauncher.Services;
using TerminalLauncher.Services.Aliases;

namespace TerminalLauncher;

/// <summary>
/// فئة «الأوامر المختصرة» في الإعدادات: قائمة الأسماء المستعارة ومحرّرها المتقدّم.
/// مفصولة عن <c>MainWindow.xaml.cs</c> كي لا يتضخّم، على نسق <c>MainWindow.Ai.cs</c>.
/// </summary>
public partial class MainWindow
{
    /// <summary>النسخة قيد التحرير (لا المحفوظة) — فالإلغاء لا يترك تعديلات نصفيّة في القائمة.</summary>
    private CommandAlias? _editingAlias;

    /// <summary>يعيد بناء قائمة الأسماء المستعارة من المخزن المشترك.</summary>
    private void BuildAliasList()
    {
        if (AliasList is null) return;

        AliasList.Children.Clear();
        IReadOnlyList<CommandAlias> aliases = AliasStore.Shared.All();

        foreach (CommandAlias alias in aliases)
            AliasList.Children.Add(BuildAliasRow(alias));

        AliasEmptyText.Visibility = aliases.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildAliasRow(CommandAlias alias)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var word = new TextBlock
        {
            Text = alias.Name,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FlowDirection = FlowDirection.LeftToRight,
            FontWeight = FontWeights.SemiBold,
            Opacity = alias.Enabled ? 1.0 : 0.5,
        };
        word.SetResourceReference(TextBlock.FontSizeProperty, "Size.Ui");
        word.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");

        // بلا وصف نعرض الأوامر نفسها: صفٌّ يقول اسماً فقط لا يذكّرك بما يفعله بعد شهر.
        var desc = new TextBlock
        {
            Text = alias.Description.Length > 0 ? alias.Description : string.Join(" · ", alias.Commands),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        desc.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");
        desc.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(word);
        text.Children.Add(desc);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        actions.Children.Add(AliasRowButton(Loc.T("alias.edit"), alias, edit: true));
        actions.Children.Add(AliasRowButton(Loc.T("alias.delete"), alias, edit: false));
        Grid.SetColumn(actions, 1);

        grid.Children.Add(text);
        grid.Children.Add(actions);

        var card = new Border
        {
            Child = grid,
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface2");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Hairline");
        return card;
    }

    private Button AliasRowButton(string label, CommandAlias alias, bool edit)
    {
        var button = new Button
        {
            Content = label,
            Tag = alias,
            Padding = new Thickness(10, 3, 10, 4),
            Margin = new Thickness(6, 0, 0, 0),
        };
        button.SetResourceReference(Control.FontSizeProperty, "Size.Small");

        if (edit) button.Click += AliasEditRow_Click;
        else button.Click += AliasDeleteRow_Click;

        return button;
    }

    private void AliasEditRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CommandAlias alias }) OpenAliasEditor(alias);
    }

    private void AliasDeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CommandAlias alias }) return;

        string? choice = Views.AppDialog.Confirm(
            this, Loc.T("alias.delete"), string.Format(Loc.T("alias.deleteConfirm"), alias.Name),
            (Loc.T("alias.delete"), "delete", Views.DialogButtonKind.Danger),
            (Loc.T("ai.prev.cancel"), "cancel", Views.DialogButtonKind.Neutral));

        if (choice != "delete") return;

        AliasStore.Shared.Delete(alias.Id);
        if (_editingAlias?.Id == alias.Id) CloseAliasEditor();
        BuildAliasList();
    }

    private void AliasAdd_Click(object sender, RoutedEventArgs e) => OpenAliasEditor(new CommandAlias());

    private void OpenAliasEditor(CommandAlias alias)
    {
        _editingAlias = alias.Clone();

        AliasNameBox.Text = _editingAlias.Name;
        AliasDescBox.Text = _editingAlias.Description;
        AliasCommandsBox.Text = string.Join("\n", _editingAlias.Commands);
        AliasShellBox.Text = _editingAlias.Shell;
        AliasConfirmCheck.IsChecked = _editingAlias.ConfirmBeforeRun;
        AliasEnabledCheck.IsChecked = _editingAlias.Enabled;

        BuildAliasVarRows();
        AliasEditor.Visibility = Visibility.Visible;
        AliasNameBox.Focus();
    }

    private void CloseAliasEditor()
    {
        _editingAlias = null;
        AliasEditor.Visibility = Visibility.Collapsed;
    }

    private void BuildAliasVarRows()
    {
        AliasVarList.Children.Clear();
        if (_editingAlias is null) return;

        foreach (AliasVariable variable in _editingAlias.Variables)
            AliasVarList.Children.Add(BuildAliasVarRow(variable));
    }

    /// <summary>
    /// صفّ متغيّر: الاسم · الوصف · القيمة الافتراضيّة · إلزاميّ · حذف.
    /// <para>الحقول تكتب في كائن المتغيّر مباشرةً (وهو ضمن النسخة قيد التحرير)، فلا حاجة لجمعها
    /// يدويّاً عند الحفظ ولا خطر نسيان حقل.</para>
    /// </summary>
    private UIElement BuildAliasVarRow(AliasVariable variable)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBox nameBox = AliasVarBox(variable.Name, Loc.T("alias.varName"), mono: true);
        TextBox labelBox = AliasVarBox(variable.Label, Loc.T("alias.varLabel"), mono: false);
        TextBox defaultBox = AliasVarBox(variable.Default, Loc.T("alias.varDefault"), mono: false);

        nameBox.TextChanged += OnNameChanged;
        labelBox.TextChanged += OnLabelChanged;
        defaultBox.TextChanged += OnDefaultChanged;

        var required = new CheckBox
        {
            IsChecked = variable.Required,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
            ToolTip = Loc.T("alias.varRequired"),
        };
        required.Checked += OnRequiredChanged;
        required.Unchecked += OnRequiredChanged;

        var remove = new Button
        {
            Content = "✕",
            Padding = new Thickness(7, 2, 7, 3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        remove.SetResourceReference(Control.FontSizeProperty, "Size.Small");
        remove.Click += OnRemoveClicked;

        var tail = new StackPanel { Orientation = Orientation.Horizontal };
        tail.Children.Add(required);
        tail.Children.Add(remove);

        Grid.SetColumn(labelBox, 1);
        Grid.SetColumn(defaultBox, 2);
        Grid.SetColumn(tail, 3);

        grid.Children.Add(nameBox);
        grid.Children.Add(labelBox);
        grid.Children.Add(defaultBox);
        grid.Children.Add(tail);
        return grid;

        void OnNameChanged(object sender, TextChangedEventArgs args)
        {
            variable.Name = nameBox.Text.Trim();
        }

        void OnLabelChanged(object sender, TextChangedEventArgs args)
        {
            variable.Label = labelBox.Text.Trim();
        }

        void OnDefaultChanged(object sender, TextChangedEventArgs args)
        {
            variable.Default = defaultBox.Text.Trim();
        }

        void OnRequiredChanged(object sender, RoutedEventArgs args)
        {
            variable.Required = required.IsChecked == true;
        }

        void OnRemoveClicked(object sender, RoutedEventArgs args)
        {
            _editingAlias?.Variables.Remove(variable);
            BuildAliasVarRows();
        }
    }

    private static TextBox AliasVarBox(string value, string hint, bool mono)
    {
        var box = new TextBox
        {
            Text = value,
            ToolTip = hint,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (mono)
        {
            box.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            box.FlowDirection = FlowDirection.LeftToRight;
        }

        return box;
    }

    private void AliasVarAdd_Click(object sender, RoutedEventArgs e)
    {
        _editingAlias?.Variables.Add(new AliasVariable());
        BuildAliasVarRows();
    }

    /// <summary>
    /// يتحقّق ثمّ يحفظ. الرفض برسالة تسمّي السبب: اسم مستعار بلا كلمة أو بلا أوامر لا يمكن
    /// استدعاؤه أصلاً، وحفظه صامتاً يترك المستخدم ينتظر شيئاً لن يحدث.
    /// </summary>
    private void AliasSave_Click(object sender, RoutedEventArgs e)
    {
        if (_editingAlias is null) return;

        string name = AliasNameBox.Text.Trim();
        if (name.Length == 0 || name.Contains(' '))
        {
            NotificationService.Warning(Loc.T("alias.title"), Loc.T("alias.errName"));
            return;
        }

        var commands = new List<string>();
        foreach (string line in AliasCommandsBox.Text.Replace("\r\n", "\n").Split('\n'))
        {
            string command = line.Trim();
            if (command.Length > 0) commands.Add(command);
        }

        if (commands.Count == 0)
        {
            NotificationService.Warning(Loc.T("alias.title"), Loc.T("alias.errCommands"));
            return;
        }

        _editingAlias.Name = name;
        _editingAlias.Description = AliasDescBox.Text.Trim();
        _editingAlias.Commands = commands;
        _editingAlias.Shell = AliasShellBox.Text.Trim();
        _editingAlias.ConfirmBeforeRun = AliasConfirmCheck.IsChecked == true;
        _editingAlias.Enabled = AliasEnabledCheck.IsChecked == true;
        _editingAlias.Variables.RemoveAll(IsNamelessVariable);

        AliasStore.Shared.Save(_editingAlias);
        CloseAliasEditor();
        BuildAliasList();
    }

    /// <summary>متغيّر بلا اسم لا يُستبدل في أيّ أمر — وجوده في الملفّ تشويش لا أكثر.</summary>
    private static bool IsNamelessVariable(AliasVariable variable) => variable.Name.Length == 0;

    private void AliasCancel_Click(object sender, RoutedEventArgs e) => CloseAliasEditor();
}
