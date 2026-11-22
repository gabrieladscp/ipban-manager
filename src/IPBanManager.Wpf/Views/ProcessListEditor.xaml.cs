using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using IPBanManager.Core.Validation;
using Microsoft.Win32;

namespace IPBanManager.Wpf.Views;

/// <summary>Processos executados pelo IPBan num evento (um por linha: caminho|argumentos).</summary>
public partial class ProcessListEditor : UserControl
{
    private static readonly Dictionary<string, string> VariableLabels = new Dictionary<string, string>
    {
        ["###IPADDRESS###"] = "IP", ["###USERNAME###"] = "Usuário", ["###SOURCE###"] = "Origem", ["###COUNT###"] = "Contagem",
    };

    public ProcessListEditor()
    {
        InitializeComponent();
        foreach (var variable in ProcessEntry.Variables)
        {
            var button = new Button
            {
                Content = VariableLabels[variable], ToolTip = variable, MinWidth = 0, Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 4, 0),
            };
            button.Click += (s, e) => Insert(variable);
            VariablesPanel.Children.Add(button);
        }
        Editor.AttachForm(Form);
        Editor.Describe = Describe;
        Editor.BuildEntry = existing =>
        {
            var error = ProcessEntry.Create(PathBox.Text, ArgsBox.Text, File.Exists, out var line);
            return (line, error);
        };
        Editor.ClearForm = () => { PathBox.Clear(); ArgsBox.Clear(); };
    }

    /// <summary>Valor do ipban.config (processos separados por quebra de linha).</summary>
    public string Value
    {
        get => string.Join("\n", Editor.Entries);
        set => Editor.Entries = (value ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    private static string Describe(string line)
    {
        var parts = line.Split(new[] { '|' }, 2);
        return parts.Length == 2 && parts[1].Length > 0 ? $"{parts[0]}   {parts[1]}" : parts[0];
    }

    private void Insert(string variable)
    {
        var index = ArgsBox.CaretIndex;
        var prefix = index > 0 && ArgsBox.Text[index - 1] != ' ' ? " " : "";
        ArgsBox.Text = ArgsBox.Text.Insert(index, prefix + variable);
        ArgsBox.CaretIndex = index + prefix.Length + variable.Length;
        ArgsBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Programa a executar",
            Filter = "Programas e scripts (*.exe;*.bat;*.cmd;*.ps1)|*.exe;*.bat;*.cmd;*.ps1|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) PathBox.Text = dialog.FileName;
    }
}
