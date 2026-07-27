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

        for (int i = 0; i < _editingAlias.Variables.Count; i++)
            AliasVarList.Children.Add(BuildAliasVarRow(_editingAlias.Variables[i], i + 1));

        // قائمة فارغة وصامتة تترك المستخدم يظنّ المتغيّرات إلزاميّة، أو لا يعرف ما هي أصلاً.
        AliasVarEmptyText.Visibility = _editingAlias.Variables.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// بطاقة متغيّر واحد: عنوانٌ برقمه، ثمّ حقلٌ في كلّ سطر مسبوقاً باسمه ومثالِه، ثمّ سطرٌ يقول
    /// كيف يُكتب هذا المتغيّر داخل الأوامر.
    ///
    /// <para><b>لماذا بطاقة لا صفٌّ من ثلاثة حقول:</b> الصفّ كان ثلاثة صناديق متجاورة بلا عناوين —
    /// عرضُ كلٍّ منها في لوحة بعرض 420 بضعُ عشرات من البكسل، ولا شيء يقول أيُّها الاسم وأيُّها
    /// القيمة الافتراضيّة. البطاقة تعطي كلّ حقل سطراً وعنواناً ومثالاً.</para>
    ///
    /// <para>الحقول تكتب في كائن المتغيّر مباشرةً (وهو ضمن النسخة قيد التحرير)، فلا حاجة لجمعها
    /// يدويّاً عند الحفظ ولا خطر نسيان حقل.</para>
    /// </summary>
    private UIElement BuildAliasVarRow(AliasVariable variable, int order)
    {
        var head = new TextBlock
        {
            Text = string.Format(Loc.T("alias.varHead"), order),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        head.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");
        head.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");

        var remove = new Button
        {
            Content = "✕",
            Padding = new Thickness(7, 1, 7, 2),
            ToolTip = Loc.T("alias.varRemove"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        remove.SetResourceReference(Control.FontSizeProperty, "Size.Small");
        remove.Click += OnRemoveClicked;

        var headRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(remove, 1);
        headRow.Children.Add(head);
        headRow.Children.Add(remove);

        TextBox nameBox = AliasVarBox(variable.Name, "alias.varNamePh", "alias.varNameTip", mono: true);
        TextBox labelBox = AliasVarBox(variable.Label, "alias.varLabelPh", "alias.varLabelTip", mono: false);
        TextBox defaultBox = AliasVarBox(variable.Default, "alias.varDefaultPh", "alias.varDefaultTip", mono: false);

        // معاينة حيّة: يكتب الاسم فيرى فوراً ما عليه كتابته في خانة الأوامر — وهو الرابط الذي
        // كان مفقوداً بين تعريف المتغيّر واستعماله. تُنشَأ قبل ربط المعالجات لأنّ OnNameChanged
        // يقرأها، وتحويل دالّة محلّيّة إلى مفوَّض يشترط أن يكون كلّ ما تلتقطه مُسنَداً سلفاً.
        var usage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        usage.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");
        usage.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        UpdateUsage();

        nameBox.TextChanged += OnNameChanged;
        labelBox.TextChanged += OnLabelChanged;
        defaultBox.TextChanged += OnDefaultChanged;

        var required = new CheckBox
        {
            IsChecked = variable.Required,
            Content = Loc.T("alias.varRequired"),
            Margin = new Thickness(0, 10, 0, 0),
        };
        required.SetResourceReference(FrameworkElement.StyleProperty, "AppCheckBox");
        required.Checked += OnRequiredChanged;
        required.Unchecked += OnRequiredChanged;

        var body = new StackPanel();
        body.Children.Add(headRow);
        body.Children.Add(AliasFieldRow("alias.varName", nameBox));
        body.Children.Add(AliasFieldRow("alias.varLabel", labelBox));
        body.Children.Add(AliasFieldRow("alias.varDefault", defaultBox));
        body.Children.Add(required);
        body.Children.Add(usage);

        var card = new Border
        {
            Child = body,
            Padding = new Thickness(12, 10, 12, 12),
            Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Hairline");
        return card;

        void UpdateUsage()
        {
            string name = nameBox.Text.Trim();
            usage.Text = name.Length == 0
                ? Loc.T("alias.varUseEmpty")
                : string.Format(Loc.T("alias.varUse"), "$" + name);
        }

        void OnNameChanged(object sender, TextChangedEventArgs args)
        {
            variable.Name = nameBox.Text.Trim();
            UpdateUsage();
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

    /// <summary>سطر «عنوانُ الحقل ثمّ الحقل» — العنوان بعرض ثابت فتصطفّ الحقول عموديّاً.</summary>
    private static UIElement AliasFieldRow(string labelKey, TextBox field)
    {
        var label = new TextBlock
        {
            Text = Loc.T(labelKey),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 8, 0),
        };
        label.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(field, 1);
        row.Children.Add(label);
        row.Children.Add(field);
        return row;
    }

    /// <summary>حقل داخل بطاقة متغيّر: مثالٌ إرشاديّ داخله، وشرحُه في تلميحه.</summary>
    private static TextBox AliasVarBox(string value, string placeholderKey, string tipKey, bool mono)
    {
        var box = new TextBox
        {
            Text = value,
            ToolTip = Loc.T(tipKey),
            Padding = new Thickness(7, 4, 7, 5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Theme.Placeholder.SetText(box, Loc.T(placeholderKey));

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

    /// <summary>
    /// أزرار «الدليل» أينما كانت: في رؤوس أقسام الإعدادات وفي شريط العنوان.
    ///
    /// <para>الوجهة تُقرأ من <c>Tag</c> لا من معالج لكلّ زرّ: قسمٌ جديد في الإعدادات يحتاج سطر
    /// XAML واحداً، ولا يبقى في الكود معالجٌ لكلّ قسم يتكاثر مع الأقسام. و<c>Tag</c> فارغ =
    /// أوّل الدليل.</para>
    /// </summary>
    private void Docs_Click(object sender, RoutedEventArgs e)
        => DocsService.Open((sender as FrameworkElement)?.Tag as string ?? "");
}
