using System.ComponentModel;
using System.Runtime.CompilerServices;
using DailyGate.Shared;

namespace DailyGate.Windows.Client.ViewModels;

public sealed class QuestionViewModel : INotifyPropertyChanged
{
    public Guid Id { get; }
    public string Text { get; }
    public QuestionKind Kind { get; }
    public string KindLabel => Kind == QuestionKind.SingleChoice ? "Один вариант" : "Можно несколько";
    public List<OptionViewModel> Options { get; }

    public QuestionViewModel(DailyQuestion question)
    {
        Id = question.Id; Text = question.Text; Kind = question.Kind;
        Options = question.Options.Select(option => new OptionViewModel(option.Id, option.Text, this)).ToList();
    }

    public void Select(OptionViewModel selected)
    {
        if (Kind != QuestionKind.SingleChoice || !selected.IsSelected) return;
        foreach (var option in Options.Where(option => option != selected)) option.IsSelected = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class OptionViewModel(Guid id, string text, QuestionViewModel question) : INotifyPropertyChanged
{
    private bool _isSelected;
    public Guid Id { get; } = id;
    public string Text { get; } = text;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
            question.Select(this);
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
