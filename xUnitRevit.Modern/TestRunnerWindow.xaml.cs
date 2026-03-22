using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Xunit;
using Xunit.Abstractions;

namespace xUnitRevit
{
  public partial class TestRunnerWindow : Window
  {
    private TestRunnerViewModel _viewModel;

    public TestRunnerWindow()
    {
      InitializeComponent();
      _viewModel = new TestRunnerViewModel();
      DataContext = _viewModel;
    }

    public void SetStartupAssemblies(List<string> assemblies)
    {
      foreach (var assembly in assemblies)
      {
        if (File.Exists(assembly))
        {
          _viewModel.LoadAssembly(assembly);
        }
      }
    }

    private void LoadAssembly_Click(object sender, RoutedEventArgs e)
    {
      var dialog = new OpenFileDialog
      {
        Filter = "Test Assemblies (*.dll)|*.dll",
        Multiselect = true
      };

      if (dialog.ShowDialog() == true)
      {
        foreach (var file in dialog.FileNames)
        {
          _viewModel.LoadAssembly(file);
        }
      }
    }

    private async void RunAll_Click(object sender, RoutedEventArgs e)
    {
      await _viewModel.RunAllTests();
    }

    private async void RunSelected_Click(object sender, RoutedEventArgs e)
    {
      await _viewModel.RunSelectedTests();
    }
  }

  public class TestCaseViewModel : INotifyPropertyChanged
  {
    private string _status = "Pending";
    private string _duration = "";
    private string _message = "";

    public string DisplayName { get; set; }
    public string AssemblyPath { get; set; }
    public ITestCase TestCase { get; set; }
    public bool IsSelected { get; set; }

    public string Status
    {
      get => _status;
      set { _status = value; OnPropertyChanged(); }
    }

    public string Duration
    {
      get => _duration;
      set { _duration = value; OnPropertyChanged(); }
    }

    public string Message
    {
      get => _message;
      set { _message = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null) =>
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }

  public class TestRunnerViewModel : INotifyPropertyChanged
  {
    private bool _isRunning;
    private int _passedCount;
    private int _failedCount;
    private int _skippedCount;

    public ObservableCollection<TestCaseViewModel> Tests { get; } = new ObservableCollection<TestCaseViewModel>();

    public bool IsRunning
    {
      get => _isRunning;
      set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTests)); }
    }

    public int TotalCount => Tests.Count;
    public int PassedCount
    {
      get => _passedCount;
      set { _passedCount = value; OnPropertyChanged(); }
    }
    public int FailedCount
    {
      get => _failedCount;
      set { _failedCount = value; OnPropertyChanged(); }
    }
    public int SkippedCount
    {
      get => _skippedCount;
      set { _skippedCount = value; OnPropertyChanged(); }
    }

    public bool HasTests => Tests.Count > 0 && !IsRunning;
    public bool HasSelectedTests => Tests.Any(t => t.IsSelected) && !IsRunning;

    public void LoadAssembly(string assemblyPath)
    {
      try
      {
        using (var controller = new XunitFrontController(
          AppDomainSupport.Denied,
          assemblyPath,
          diagnosticMessageSink: new NullMessageSink()))
        {
          var discoveryVisitor = new TestDiscoveryVisitor();
          controller.Find(false, discoveryVisitor, TestFrameworkOptions.ForDiscovery());
          discoveryVisitor.Finished.WaitOne();

          foreach (var testCase in discoveryVisitor.TestCases)
          {
            Tests.Add(new TestCaseViewModel
            {
              DisplayName = testCase.DisplayName,
              AssemblyPath = assemblyPath,
              TestCase = testCase
            });
          }
        }

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasTests));
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Failed to load assembly: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    public async Task RunAllTests()
    {
      IsRunning = true;
      PassedCount = 0;
      FailedCount = 0;
      SkippedCount = 0;

      foreach (var test in Tests)
      {
        test.Status = "Pending";
        test.Duration = "";
        test.Message = "";
      }

      var assemblies = Tests.Select(t => t.AssemblyPath).Distinct();

      foreach (var assemblyPath in assemblies)
      {
        await RunTestsInAssembly(assemblyPath, Tests.Where(t => t.AssemblyPath == assemblyPath).ToList());
      }

      IsRunning = false;
    }

    public async Task RunSelectedTests()
    {
      IsRunning = true;
      var selected = Tests.Where(t => t.IsSelected).ToList();

      foreach (var assemblyPath in selected.Select(t => t.AssemblyPath).Distinct())
      {
        await RunTestsInAssembly(assemblyPath, selected.Where(t => t.AssemblyPath == assemblyPath).ToList());
      }

      IsRunning = false;
    }

    private Task RunTestsInAssembly(string assemblyPath, List<TestCaseViewModel> tests)
    {
      return Task.Run(() =>
      {
        using (var controller = new XunitFrontController(
          AppDomainSupport.Denied,
          assemblyPath,
          diagnosticMessageSink: new NullMessageSink()))
        {
          var executionVisitor = new TestExecutionVisitor(tests, this);
          var testCases = tests.Select(t => t.TestCase).ToList();
          controller.RunTests(testCases, executionVisitor, TestFrameworkOptions.ForExecution());
          executionVisitor.Finished.WaitOne();
        }
      });
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null) =>
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }

  internal class TestDiscoveryVisitor : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
  {
    public List<ITestCase> TestCases { get; } = new List<ITestCase>();
    public System.Threading.ManualResetEvent Finished { get; } = new System.Threading.ManualResetEvent(false);

    public bool OnMessage(IMessageSinkMessage message)
    {
      if (message is ITestCaseDiscoveryMessage discovery)
      {
        TestCases.Add(discovery.TestCase);
      }

      if (message is IDiscoveryCompleteMessage)
      {
        Finished.Set();
      }

      return true;
    }
  }

  internal class TestExecutionVisitor : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
  {
    private readonly List<TestCaseViewModel> _tests;
    private readonly TestRunnerViewModel _viewModel;
    public System.Threading.ManualResetEvent Finished { get; } = new System.Threading.ManualResetEvent(false);

    public TestExecutionVisitor(List<TestCaseViewModel> tests, TestRunnerViewModel viewModel)
    {
      _tests = tests;
      _viewModel = viewModel;
    }

    public bool OnMessage(IMessageSinkMessage message)
    {
      if (message is ITestPassed passed)
      {
        var test = _tests.FirstOrDefault(t => t.TestCase.DisplayName == passed.TestCase.DisplayName);
        if (test != null)
        {
          test.Status = "Passed";
          test.Duration = $"{passed.ExecutionTime:F3}s";
        }
        _viewModel.PassedCount++;
      }
      else if (message is ITestFailed failed)
      {
        var test = _tests.FirstOrDefault(t => t.TestCase.DisplayName == failed.TestCase.DisplayName);
        if (test != null)
        {
          test.Status = "Failed";
          test.Duration = $"{failed.ExecutionTime:F3}s";
          test.Message = string.Join(Environment.NewLine, failed.Messages);
        }
        _viewModel.FailedCount++;
      }
      else if (message is ITestSkipped skipped)
      {
        var test = _tests.FirstOrDefault(t => t.TestCase.DisplayName == skipped.TestCase.DisplayName);
        if (test != null)
        {
          test.Status = "Skipped";
          test.Message = skipped.Reason;
        }
        _viewModel.SkippedCount++;
      }
      else if (message is ITestAssemblyFinished)
      {
        Finished.Set();
      }

      return true;
    }
  }

  internal class NullMessageSink : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
  {
    public bool OnMessage(IMessageSinkMessage message) => true;
  }
}
