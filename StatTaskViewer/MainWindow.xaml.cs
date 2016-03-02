using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.TeamFoundation.Server;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using StatTaskViewer.Annotations;
using TfsApi.Administration;
using TfsApi.Administration.Contracts;
using TfsApi.Administration.Dto;
using TfsApi.Queries;
using TfsApi.Queries.Contracts;

namespace StatTaskViewer
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public static readonly DependencyProperty CurrentUserProperty =
            DependencyProperty.Register("CurrentUser", typeof(string), typeof(MainWindow),
                new PropertyMetadata(default(string)));

        public static readonly DependencyProperty CurrentStaterProperty =
            DependencyProperty.Register("CurrentState", typeof(string), typeof(MainWindow),
                new PropertyMetadata(default(string)));

        private readonly object waitTask = new object();
        public ObservableCollection<ProjectCollection> TempCollections;

        private ObservableCollection<ProjectCollection> _collection;
        private string _loggedInUser = String.Empty;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Collections = null;
            TempCollections = new ObservableCollection<ProjectCollection>();

            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
        }

        private ObservableCollection<TeamFoundationIdentity> _users;
        public ObservableCollection<TeamFoundationIdentity> Users
        {
            get { return _users; }
            set
            {
                _users = value;
                OnPropertyChanged();
            }
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

        public string CurrentUser
        {
            get { return (string)GetValue(CurrentUserProperty); }
            set { SetValue(CurrentUserProperty, value); }
        }

        public string CurrentState
        {
            get { return (string)GetValue(CurrentStaterProperty); }
            set { SetValue(CurrentStaterProperty, value); }
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

            await GetAvailableUsers();

            await SortByProjects();
        }

        /// <summary>
        ///     Кладет результат в <see cref="TempCollections" />, и имя пользователя в <see cref="_loggedInUser" />
        /// </summary>
        /// <returns>использовать в async методах, с await. Либо GetData().Wait()</returns>
        private Task GetData()
        {
            lock (waitTask)
            {
                TempCollections.Clear();

                return Task.Factory.StartNew((() =>
                {
                    ITeamProjectCollections smth = TeamProjectCollectionFactory.CreateTeamProjectCollectionMananger(
                        new Uri(ConfigurationManager.AppSettings["TfsUriShort"]));


                    var versionControl = smth.TfsTeamProjectCollection.GetService<VersionControlServer>();

                    _loggedInUser = versionControl.AuthorizedIdentity.DisplayName;

                    foreach (Collection collection in smth.ListCollections())
                    {
                        var pc = new ProjectCollection { CollectionName = collection.CollectionName };

                        TfsTeamProjectCollection teamProjectCollection =
                            smth.TfsConfigurationServer.GetTeamProjectCollection(collection.TeamProjectCollectionID);

                        var structureService = (ICommonStructureService)teamProjectCollection.GetService(typeof(ICommonStructureService));

                        var projectInfoList = new List<ProjectInfo>(structureService.ListAllProjects());

                        pc.ProjectsList = projectInfoList.ToList();

                        IQueryRunner query = QueryRunnerFactory.CreateInstance(collection.Url,
                            collection.CollectionName);

                        WorkItemCollection answer = query.Execute(
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

                        foreach (WorkItem workItem in answer)
                        {
                            var wiEx = new WorkItemEx(workItem);

                            if (workItem.Fields.Contains("Remaining Work"))
                            {
                                if (workItem.Fields["Remaining Work"].Value != null)
                                    wiEx.RemainingWork = (double)workItem.Fields["Remaining Work"].Value;
                            }

                            // getting effort
                            {
                                if (workItem.Links.Count != 0)
                                {
                                    var ids = new List<int>();
                                    for (int i = 0; i < workItem.Links.Count; i++)
                                    {
                                        var relatedLink = (workItem.Links[i] as RelatedLink);

                                        if (relatedLink != null)
                                        {
                                            int id = relatedLink.RelatedWorkItemId;
                                            ids.Add(id);
                                        }

                                        string q =
                                            "SELECT * FROM WorkItems " +
                                            "WHERE [System.WorkItemType] = 'Product Backlog Item' " +
                                            "AND [System.Id] in (" + String.Join(",", ids) + ")" +
                                            " ORDER BY [System.Id]";
                                        WorkItemCollection bLogs = query.Execute(q);

                                        if (bLogs.Count != 0)
                                        {
                                            if (bLogs[0].Fields.Contains("Effort"))
                                            {
                                                if (bLogs[0].Fields["Effort"].Value != null)
                                                    wiEx.Effort = (double)bLogs[0].Fields["Effort"].Value;
                                            }

                                            if (bLogs[0].Fields.Contains("Business Value"))
                                            {
                                                if (bLogs[0].Fields["Business Value"].Value != null)
                                                    wiEx.BusinessValue = (double)(int)bLogs[0].Fields["Business Value"].Value;
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

                        TempCollections.Add(pc);
                    }
                }));
            }
        }



        private Task GetAvailableUsers()
        {
            lock (waitTask)
            {
                return Task.Factory.StartNew((() =>
                {
                    ITeamProjectCollections smth = TeamProjectCollectionFactory.CreateTeamProjectCollectionMananger(
                        new Uri(ConfigurationManager.AppSettings["TfsUriShort"]));


                    var versionControl = smth.TfsTeamProjectCollection.GetService<VersionControlServer>();

                    _loggedInUser = versionControl.AuthorizedIdentity.DisplayName;



                    foreach (Collection collection in smth.ListCollections())
                    {
                        TfsTeamProjectCollection teamProjectCollection =
                            smth.TfsConfigurationServer.GetTeamProjectCollection(collection.TeamProjectCollectionID);

                        //users list
                        var identityManagementService = teamProjectCollection.GetService<IIdentityManagementService2>();
                        var validUsers =
                            identityManagementService.ReadIdentities(IdentitySearchFactor.AccountName,
                                new[] {"Project Collection Valid Users"}, MembershipQuery.Expanded,
                                ReadIdentityOptions.None)[0][0].Members;
                        var users =
                            identityManagementService.ReadIdentities(validUsers, MembershipQuery.None,
                                ReadIdentityOptions.None).Where(x => !x.IsContainer).ToArray();


                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (Users == null)Users = new ObservableCollection<TeamFoundationIdentity>();
                            foreach (var user in users)
                            {
                                if(!Users.Any(u=>u.UniqueName.Equals(user.UniqueName)))
                                    Users.Add(user);
                            }

                        });
                    }
                }));
            }
        }

        private void SortByCollections()
        {
            Collections = new ObservableCollection<ProjectCollection>();
            foreach (ProjectCollection collection in TempCollections)
            {
                Collections.Add(collection);
            }

            ListBox.SelectedIndex = 0;
        }


        private Task SortByProjects(bool includeZero = false)
        {
            var projects = new List<ProjectCollection>();
            return Task.Factory.StartNew((() =>
            {

                foreach (ProjectCollection collection in TempCollections)
                {
                    //Project[] distProjects = collection.WorkItems.Select(wi => wi.WorkItem.Project).Distinct().ToArray();

                    for (int i = 0; i < collection.ProjectsList.Count(); i++)
                    {
                        var pCollection = new ProjectCollection
                        {
                            CollectionName = collection.ProjectsList[i].Name,
                            WorkItems =
                                collection.WorkItems.Where(wi => wi.WorkItem.Project.Name.Equals(collection.ProjectsList[i].Name)).ToList()
                        };
                        if (pCollection.WorkItems.Count == 0 && !includeZero)
                            continue;

                        projects.Add(pCollection);
                    }
                }
            })).ContinueWith(t =>
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
                foreach (ProjectCollection collection in projects)
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
                if (Collections != null) Collections.Clear();
                Collections = null;
            }

            await GetData();

            await SortByProjects();
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                lock (waitTask)
                {
                    if (Collections != null) Collections.Clear();
                    Collections = null;
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
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }


        private void Calendar_OnSelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            var calendar = sender as System.Windows.Controls.Calendar;
            if (calendar != null && calendar.SelectedDates.Any())
            {
                TextBoxPeriod.Text = calendar.SelectedDates.OrderBy(t => t.Date).First().Date.ToShortDateString() +
                                     " - " + calendar.SelectedDates.OrderBy(t => t.Date).Last().Date.ToShortDateString();
            }
        }


        private void ApplyFilter_BtnClick(object sender, RoutedEventArgs e)
        {

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

        public override string ToString()
        {
            return CollectionName;
        }

        public List<ProjectInfo> ProjectsList { get; set; }

    }

    /// <summary>
    /// Класс-контейнер, нужен для отображения доп. полей на экране, потому что майкрософт запечатали класс <see cref="WorkItem"/>
    /// </summary>
    public class WorkItemEx
    {
        public WorkItemEx(WorkItem item)
        {
            WorkItem = item;
        }
        public WorkItem WorkItem { get; set; }

        public double RemainingWork { get; set; }
        public double Effort { get; set; }
        public double BusinessValue { get; set; }
    }
}