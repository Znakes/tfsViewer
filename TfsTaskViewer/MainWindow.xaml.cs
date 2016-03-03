using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Server;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using TfsApi.Administration;
using TfsApi.Queries;
using TfsTaskViewer.Annotations;
using TfsTaskViewer.Properties;

namespace TfsTaskViewer
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public static readonly DependencyProperty CurrentUserProperty =
            DependencyProperty.Register("CurrentUser", typeof (string), typeof (MainWindow),
                new PropertyMetadata(default(string)));

        public static readonly DependencyProperty CurrentStateProperty =
            DependencyProperty.Register("CurrentState", typeof (string), typeof (MainWindow),
                new PropertyMetadata(default(string)));

        private readonly object waitTask = new object();
        private volatile bool IsBusy = false;

        private ObservableCollection<ProjectCollection> _collection;
        private string _loggedInUser = string.Empty;
        private List<ProjectCollection> _tempCollections;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Collections = new ObservableCollection<ProjectCollection>();
            _tempCollections = new List<ProjectCollection>();

            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
        }

        public ObservableCollection<ProjectCollection> Collections
        {
            get { return _collection; }
            set
            {
                _collection = value;
                OnPropertyChanged();
            }
        }

        public string CurrentState
        {
            get { return (string) GetValue(CurrentStateProperty); }
            set { SetValue(CurrentStateProperty, value); }
        }

        public string CurrentUser
        {
            get { return (string) GetValue(CurrentUserProperty); }
            set { SetValue(CurrentUserProperty, value); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Current_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
        }

        private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            await GetData();

            CurrentUser = _loggedInUser;

            await SortByProjects();
        }

        public void inUI(Action action, bool asyncronous = false)
        {
            if (asyncronous)
                Application.Current.Dispatcher.Invoke(action);
            else
                Application.Current.Dispatcher.InvokeAsync(action);
        }

        /// <summary>
        ///     Кладет результат в <see cref="_tempCollections" />, и имя пользователя в <see cref="_loggedInUser" />
        /// </summary>
        /// <returns>использовать в async методах, с await. Либо GetData().Wait()</returns>
        private Task GetData()
        {
            IsBusy = true;
            lock (waitTask)
            {
                _tempCollections.Clear();

                return Task.Factory.StartNew(() =>
                {
                    inUI(() => { CurrentState = "Connecting.."; });

                    var smth = TeamProjectCollectionFactory.CreateTeamProjectCollectionMananger(
                        new Uri(ConfigurationManager.AppSettings["TfsUriShort"]));


                    var versionControl = smth.TfsTeamProjectCollection.GetService<VersionControlServer>();

                    _loggedInUser = versionControl.AuthorizedIdentity.DisplayName;

                    inUI(() => { CurrentState = "Starting getting list collection"; });
                    var collectionNUm = 0;

                    var collections = smth.ListCollections();
                    foreach (var collection in collections)
                    {
                        collectionNUm++;
                        var pc = new ProjectCollection {CollectionName = collection.CollectionName};

                        var teamProjectCollection =
                            smth.TfsConfigurationServer.GetTeamProjectCollection(collection.TeamProjectCollectionID);

                        var structureService =
                            (ICommonStructureService) teamProjectCollection.GetService(typeof (ICommonStructureService));

                        var projectInfoList = new List<ProjectInfo>(structureService.ListAllProjects());

                        pc.ProjectsList = projectInfoList.ToList();

                        var query = QueryRunnerFactory.CreateInstance(collection.Url, collection.CollectionName);

                        var um = collectionNUm;
                        inUI(
                            () =>
                            {
                                CurrentState = $@"Collection: {collection.CollectionName} ({um} of {collections.Count})";
                            });

                        var answer = query.Execute(
                            "SELECT * FROM WorkItems " +
                            "WHERE [System.AssignedTo] =  '" + _loggedInUser + "'" +
                            //"AND [System.WorkItemType] <> 'Bug' " +
                            //"AND [System.WorkItemType] <> 'Product Backlog Item' " +
                            //"AND [System.WorkItemType] <> 'Test Case' " +
                            "AND [System.WorkItemType] = 'Task' " +
                            "AND [System.State] <> 'Closed' " +
                            "AND [System.State] <> 'Done' " +
                            "AND [System.State] <> 'Resolved' " +
                            "AND [System.State] <> 'Removed' " +
                            "ORDER BY [System.Id]");

                        var service = teamProjectCollection.GetService<TswaClientHyperlinkService>();

                        foreach (WorkItem workItem in answer)
                        {
                            var wiEx = new WorkItemEx {WorkItem = workItem};
                            wiEx.TaskUrl = service.GetWorkItemEditorUrl(workItem.Id).ToString();

                            if (workItem.Fields.Contains("Remaining Work"))
                            {
                                if (workItem.Fields["Remaining Work"].Value != null)
                                    wiEx.RemainingWork = (double) workItem.Fields["Remaining Work"].Value;
                            }

                            // getting effort
                            {
                                if (workItem.Links.Count != 0)
                                {
                                    var ids = new List<int>();
                                    for (var i = 0; i < workItem.Links.Count; i++)
                                    {
                                        var relatedLink = workItem.Links[i] as RelatedLink;

                                        if (relatedLink != null)
                                        {
                                            var id = relatedLink.RelatedWorkItemId;
                                            ids.Add(id);


                                            var q =
                                                "SELECT * FROM WorkItems " +
                                                "WHERE [System.WorkItemType] = 'Product Backlog Item' " +
                                                "AND [System.Id] in (" + string.Join(",", ids) + ")" +
                                                " ORDER BY [System.Id]";
                                            var bLogs = query.Execute(q);

                                            if (bLogs.Count != 0)
                                            {
                                                if (bLogs[0].Fields.Contains("Effort"))
                                                {
                                                    if (bLogs[0].Fields["Effort"].Value != null)
                                                        wiEx.Effort = (double) bLogs[0].Fields["Effort"].Value;
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            //if (workItem.Fields.Contains("Backlog Priority"))
                            //{
                            //    if (workItem.Fields["Backlog Priority"].Value != null)
                            //        wiEx.BacklogPriority = (double) workItem.Fields["Backlog Priority"].Value;
                            //}


                            pc.WorkItems.Add(wiEx);
                        }

                        _tempCollections.Add(pc);
                    }
                    inUI(() => { CurrentState = "Done!"; });
                    Console.WriteLine("FINISHED");

                    IsBusy = false;
                });
            }
        }

        private void SortByCollections()
        {
            Collections = new ObservableCollection<ProjectCollection>();
            foreach (var collection in _tempCollections)
            {
                Collections.Add(collection);
            }

            ListBox.SelectedIndex = 0;
        }


        private Task SortByProjects(bool includeZero = false)
        {
            var projects = new List<ProjectCollection>();
            return Task.Factory.StartNew(() =>
            {
                foreach (var collection in _tempCollections)
                {
                    //Project[] distProjects = collection.WorkItems.Select(wi => wi.WorkItem.Project).Distinct().ToArray();

                    for (var i = 0; i < collection.ProjectsList.Count(); i++)
                    {
                        var pCollection = new ProjectCollection
                        {
                            CollectionName = collection.ProjectsList[i].Name,
                            WorkItems =
                                collection.WorkItems.Where(
                                    wi => wi.WorkItem.Project.Name.Equals(collection.ProjectsList[i].Name)).ToList()
                        };
                        if (pCollection.WorkItems.Count == 0 && !includeZero)
                            continue;

                        projects.Add(pCollection);
                    }
                }

                var allCollection = new ProjectCollection
                {
                    CollectionName = "All",
                    WorkItems =
                                _tempCollections.SelectMany( tc => tc.WorkItems.ToArray() ).ToList()
                };

                projects.Insert(0,allCollection);


            }).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    File.AppendAllText(@"log.txt", t.Exception.Message);
                    Exception ex = t.Exception;
                    while (ex is AggregateException && ex.InnerException != null)
                        ex = ex.InnerException;
                    File.AppendAllText(@"log.txt", "Error: " + ex.Message + Environment.NewLine);
                }

                Collections = new ObservableCollection<ProjectCollection>();
                foreach (var collection in projects)
                {
                    Collections.Add(collection);
                }

                ListBox.SelectedIndex = 0;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnLogout(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Перейдите в Панель управления->(Учетные записи и Семейная безопасность)->Диспетчер учетных данных и во вкладке Учетные записи Windows удалите запись с именем TFS",
                "Инструкция", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void OnConnect(object sender, RoutedEventArgs e)
        {
            lock (waitTask)
            {
                Collections.Clear();
                Collections = null;
            }

            await GetData();

            await SortByProjects();
        }


        private Task taskGettingList = null;

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5 && !IsBusy)
            {
                lock (waitTask)
                {
                    Collections.Clear();
                    Collections = new ObservableCollection<ProjectCollection>();
                }

                await GetData();
                await SortByProjects();
            }
        }

        private void OnByCollections(object sender, RoutedEventArgs e)
        {
            SortByCollections();
        }

        private async void OnByProjects(object sender, RoutedEventArgs e)
        {
            await SortByProjects();
        }

        private async void OnByAllProjects(object sender, RoutedEventArgs e)
        {
            await SortByProjects(true);
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Вызов диалога экспорта
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnPeriodExporting(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new ExportDialog() { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                dlg.ShowDialog();
            }
            catch (Exception)
            {
                
                throw;
            }
            

            
        }

        private void MenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            var pathToNew = Settings.Default.UpdatePath;

            if (!File.Exists(pathToNew))
            {
                MessageBox.Show("Can't find new version. Check path to file in settings.");
            }
            
            string assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            var versionToUpdate = AssemblyName.GetAssemblyName(pathToNew).Version;
            var versionCurrent = new Version(assemblyVersion);

            Console.WriteLine(versionToUpdate);

            if (versionToUpdate.CompareTo(versionCurrent) <= 0)
            {
                MessageBox.Show("No updates, sorry.");
            }
            else
            {
                if (
                    MessageBox.Show(
                        $"New version of TfsViewer ({versionToUpdate}) is available. Current: {assemblyVersion}.\nDo you want to update?",
                        "New version!", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    var currentLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;

                    try
                    {
                        File.Copy(pathToNew, currentLocation + "_",true);
                    }
                    catch (Exception ex)
                    {
                        
                        throw;
                    }
                    

                    var query = $"{Directory.GetCurrentDirectory()}\\update.bat";

                    //ExecuteCommand(query);

                    System.Diagnostics.Process.Start(query);

                }
            }
            
        }

        void ExecuteCommand(string command)
        {
            int exitCode;
            ProcessStartInfo processInfo;
            Process process;

            processInfo = new ProcessStartInfo("cmd.exe", "/c " + command);
            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;
            processInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            // *** Redirect the output ***
            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardOutput = true;

            process = Process.Start(processInfo);
            process.WaitForExit();

            // *** Read the streams ***
            // Warning: This approach can lead to deadlocks, see Edit #2
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            exitCode = process.ExitCode;

            Console.WriteLine("output>>" + (String.IsNullOrEmpty(output) ? "(none)" : output));
            Console.WriteLine("error>>" + (String.IsNullOrEmpty(error) ? "(none)" : error));
            Console.WriteLine("ExitCode: " + exitCode.ToString(), "ExecuteCommand");
            process.Close();
        }

        private void MenuItem_About(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                $"Dev.: https://github.com/Znakes/tfsViewer \nCurrent ver.:{Assembly.GetExecutingAssembly().GetName().Version}");
        }
    }

    public class ProjectCollection
    {
        public ProjectCollection()
        {
            WorkItems = new List<WorkItemEx>();
        }

        public List<WorkItemEx> WorkItems { get; set; }
        public string CollectionName { get; set; }

        public List<ProjectInfo> ProjectsList { get; set; }

        public override string ToString()
        {
            return CollectionName;
        }
    }

    public class WorkItemEx
    {
        public WorkItem WorkItem { get; set; }

        public string TaskUrl { get; set; }
        public double RemainingWork { get; set; }
        public double Effort { get; set; }

        /// <summary>
        /// Переход на страничку
        /// </summary>
        public ICommand GoToUrl
        {
            get { return new FuncCommand<string>(null, url => { Process.Start(url); }); }
        }
    }

    public class FuncCommand<TParameter> : ICommand
    {
        private readonly Predicate<TParameter> canExecute;
        private readonly Action<TParameter> execute;

        public FuncCommand(Predicate<TParameter> canExecute, Action<TParameter> execute)
        {
            this.canExecute = canExecute;
            this.execute = execute;
        }

        public bool CanExecute(object parameter)
        {
            if (canExecute == null) return true;

            return canExecute((TParameter) parameter);
        }

        public void Execute(object parameter)
        {
            execute((TParameter) parameter);
        }

        public event EventHandler CanExecuteChanged;
    }
}