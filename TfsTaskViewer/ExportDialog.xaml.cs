using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Server;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.Win32;
using TfsApi.Administration;
using TfsApi.Queries;

namespace TfsTaskViewer
{
    /// <summary>
    /// Interaction logic for ExportDialog.xaml
    /// </summary>
    public partial class ExportDialog : Window
    {
        public ExportDialog()
        {
            InitializeComponent();
        }

        private void ExportDialog_OnLoaded(object sender, RoutedEventArgs e)
        {
            FinishDate.SelectedDate = DateTime.Now;
            StartDate.SelectedDate = null;
        }

        /// <summary>
        /// запуск процедуры экспорта
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            if (FinishDate.SelectedDate == null || StartDate.SelectedDate == null)
            {
                MessageBox.Show("Выберите даты");
                return;
            }

            if (FinishDate.SelectedDate < StartDate.SelectedDate)
            {
                MessageBox.Show("Начало периода всегда меньше конца.");
                return;
            }

            var dlg = new SaveFileDialog()
            {
                Filter = "Txt|*.txt"
            };

            if (dlg.ShowDialog(this) == true)
            {
                File.WriteAllText(dlg.FileName, await GetTasksByPeriod(StartDate.SelectedDate.Value, FinishDate.SelectedDate.Value));

                MessageBox.Show("Завершено!");
            }
        }

        private async Task<String> GetTasksByPeriod(DateTime start, DateTime finish)
        {
            return await GetData(start,finish);

        }

        private volatile bool IsBusy = false;
        object waitTask = new object();
        private string _loggedInUser;

        private Task<string> GetData(DateTime start, DateTime finish)
        {
            IsBusy = true;

            List<string> tasks = new List<string>(100);
            
            lock (waitTask)
            {
                return Task.Factory.StartNew(() =>
                {
                    var smth = TeamProjectCollectionFactory.CreateTeamProjectCollectionMananger(
                        new Uri(ConfigurationManager.AppSettings["TfsUriShort"]));


                    var versionControl = smth.TfsTeamProjectCollection.GetService<VersionControlServer>();

                    _loggedInUser = versionControl.AuthorizedIdentity.DisplayName;

                    var collectionNUm = 0;

                    var collections = smth.ListCollections();
                    foreach (var collection in collections)
                    {

                        tasks.Add($"------------{collection.CollectionName}----------------");
                        var pc = new ProjectCollection { CollectionName = collection.CollectionName };

                        var teamProjectCollection =
                            smth.TfsConfigurationServer.GetTeamProjectCollection(collection.TeamProjectCollectionID);

                        var structureService =
                            (ICommonStructureService)teamProjectCollection.GetService(typeof(ICommonStructureService));

                        var projectInfoList = new List<ProjectInfo>(structureService.ListAllProjects());

                        pc.ProjectsList = projectInfoList.ToList();

                        var query = QueryRunnerFactory.CreateInstance(collection.Url, collection.CollectionName);
                        WorkItemCollection answer = null;
                        string queryStr = $@"SELECT * FROM WorkItems  " +
                                          $"WHERE [System.AssignedTo] =  '{_loggedInUser}' " +
                                          $"  AND" +
                                          $"( " + //1

                                          $"([System.WorkItemType] = \'Product Backlog Item\' " +
                                          $"AND [Microsoft.VSTS.Scheduling.StartDate] >= '{start.Date.ToString(CultureInfo.InvariantCulture)}' ) " +

                                          $"OR ([System.WorkItemType] = 'Task' " +
                                          $"AND " +
                                          //$"( [System.CreatedDate] >= '{start.Date.ToString(CultureInfo.InvariantCulture)}' OR" +
                                          $" ([Microsoft.VSTS.Common.ClosedDate] <= '{finish.Date.ToString()}' " +
                                          $"AND [Microsoft.VSTS.Common.ClosedDate] >= '{start.Date.ToString()}' ) ) " +
                                          $")" + //1
                                          "ORDER BY [System.Id]";
                        try
                        {
                            answer = query.Execute(queryStr);
                        }
                        catch (Exception ex)
                        {
                        }
                        

                        var service = teamProjectCollection.GetService<TswaClientHyperlinkService>();

                        if (answer != null)
                            foreach (WorkItem workItem in answer)
                            {
                                var wiEx = new WorkItemEx { WorkItem = workItem };
                                wiEx.TaskUrl = service.GetWorkItemEditorUrl(workItem.Id).ToString();

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
                                                            wiEx.Effort = (double)bLogs[0].Fields["Effort"].Value;
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

                                tasks.Add(wiEx.WorkItem.Title);
                            }
                    }


                    

                    IsBusy = false;

                    return String.Join("\n", tasks.ToArray());
                });
            }
        }


    }
}
