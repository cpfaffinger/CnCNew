using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CnCNew.Launcher.Shell;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}

/// <summary>One row of the checks list.</summary>
public sealed class CheckItem(CheckResult result)
{
    public string Title => result.Title;
    public string Detail => result.Detail;
    public string Status => result.Status.ToString();
}

/// <summary>One row of the step list.</summary>
public sealed class StepItem(int number, StepStatus status) : ObservableObject
{
    private StepStatus _status = status;

    public int Number { get; } = number;
    public StepId Id => _status.Id;
    public string Title => _status.Title;
    public string State => _status.State.ToString();
    public string Detail => _status.Detail;

    public void Update(StepStatus status)
    {
        _status = status;
        Raise(nameof(State));
        Raise(nameof(Detail));
    }
}
