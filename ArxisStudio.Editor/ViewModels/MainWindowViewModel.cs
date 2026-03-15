using ArxisStudio.Markup;
using ArxisStudio.Markup.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using ArxisStudio.Markup.Json.Loader;
using ArxisStudio.Editor.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using ArxisStudio.Editor.Models;
using ArxisStudio.Markup.Json.Loader.Models;
using System.Threading.Tasks;
using ArxisStudio.Designer.Abstractions;
using ArxisStudio.Designer.Services;

namespace ArxisStudio.Editor.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ProjectDiscoveryService _projectDiscoveryService = new();
        private bool _suppressJsonTextChanged;
        private bool _selectionSyncInProgress;
        private string? _currentDocumentPath;
        private Process? _runningProcess;
        private Dictionary<string, UiNode> _uiNodesByPath = new();
        private Dictionary<string, ControlNode> _controlNodesByPath = new();

        [ObservableProperty]
        private string _jsonText = "";

        [ObservableProperty]
        private Control? _renderedContent;

        [ObservableProperty]
        private string _errorMessage = "";
        
        // --- Свойства конструктора ---
        
        [ObservableProperty]
        private ControlNode? _controlTreeRoot;
        
        [ObservableProperty]
        private ControlNode? _selectedNode; 
        
        [ObservableProperty]
        private ObservableCollection<PropertyItem> _editableProperties = new();

        [ObservableProperty]
        private string _projectPathInput = "";

        [ObservableProperty]
        private string _projectSummary = "No project loaded.";

        [ObservableProperty]
        private ProjectContext? _loadedProject;

        [ObservableProperty]
        private ProjectFileItem? _selectedProjectFile;

        [ObservableProperty]
        private string _workspaceMode = "Designer";

        [ObservableProperty]
        private double _previewZoom = 1.0;

        [ObservableProperty]
        private string _runOutput = "";

        [ObservableProperty]
        private string _runStatus = "No process started.";

        [ObservableProperty]
        private bool _isProjectRunning;

        [ObservableProperty]
        private double _previewSurfaceWidth = 1280;

        [ObservableProperty]
        private double _previewSurfaceHeight = 800;

        [ObservableProperty]
        private UiDocument? _currentDocument;

        public bool IsDesignerMode => WorkspaceMode == "Designer";

        public bool IsSourceMode => WorkspaceMode == "Source";

        public bool IsSplitMode => WorkspaceMode == "Split";

        public string PreviewZoomPercent => $"{PreviewZoom * 100:0}%";

        public bool CanRunProject => !IsProjectRunning && !string.IsNullOrWhiteSpace(ProjectPathInput);

        public bool CanStopProject => IsProjectRunning;

        public IDesignerPreviewBuilder DesignerPreviewBuilder { get; } = new EditorDesignerPreviewBuilder();

        public IDesignerSelectionService DesignerSelectionService { get; } = new DesignerSelectionService();

        public ObservableCollection<ProjectFileItem> ProjectArxuiFiles { get; } = new();

        // Коллекция доступных контролов (для Toolbox)
        public ObservableCollection<JObject> AvailableControls { get; } = new()
        {
            new JObject
            {
                { "TypeName", "Avalonia.Controls.Button" },
                { "Properties", new JObject 
                    {
                        { "Content", "New Button" },
                        { "Width", 100 } 
                    }
                }
            },
            new JObject
            {
                { "TypeName", "Avalonia.Controls.StackPanel" },
                { "Properties", new JObject 
                    {
                        { "Orientation", "Vertical" },
                        { "Children", new JArray() } 
                    }
                }
            },
            new JObject
            {
                { "TypeName", "Avalonia.Controls.TextBlock" },
                { "Properties", new JObject 
                    {
                        { "Text", "New TextBlock" },
                        { "FontSize", 16 }
                    }
                }
            },
            new JObject
            {
                { "TypeName", "Avalonia.Controls.Border" },
                { "Properties", new JObject 
                    {
                        { "Width", 150 },
                        { "Height", 50 },
                        { "BorderBrush", "Gray" },
                        { "BorderThickness", 1 }
                    }
                }
            }
        };

        public MainWindowViewModel()
        {
            ProjectPathInput = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ArxisStudio.Template", "ArxisStudio.Markup.Template.csproj"));
            DesignerService.Instance.JsonChanged += OnJsonChanged;
            DesignerSelectionService.SelectedNodeChanged += OnDesignerSelectionChanged;
        }

        private void OnJsonChanged(DesignerChangeKind changeKind)
        {
            UpdateUIFromRootModel(changeKind == DesignerChangeKind.Structure);
        }

        partial void OnJsonTextChanged(string value)
        {
            if (_suppressJsonTextChanged)
            {
                return;
            }

            UpdateTreeAndUIFromText();
        }

        partial void OnSelectedNodeChanged(ControlNode? oldValue, ControlNode? newValue)
        {
            LoadProperties(newValue);
            SyncPreviewSelectionFromOutline(newValue);
            // Явно оповещаем команду об изменении SelectedNode
            DeleteSelectedControlCommand.NotifyCanExecuteChanged(); 
        }

        partial void OnSelectedProjectFileChanged(ProjectFileItem? oldValue, ProjectFileItem? newValue)
        {
            OpenSelectedProjectFileCommand.NotifyCanExecuteChanged();
        }

        partial void OnLoadedProjectChanged(ProjectContext? value)
        {
            if (DesignerPreviewBuilder is EditorDesignerPreviewBuilder builder)
            {
                builder.ProjectContext = value;
            }

            RunProjectCommand.NotifyCanExecuteChanged();
        }

        partial void OnProjectPathInputChanged(string value)
        {
            OnPropertyChanged(nameof(CanRunProject));
            RunProjectCommand.NotifyCanExecuteChanged();
        }

        partial void OnWorkspaceModeChanged(string value)
        {
            OnPropertyChanged(nameof(IsDesignerMode));
            OnPropertyChanged(nameof(IsSourceMode));
            OnPropertyChanged(nameof(IsSplitMode));
        }

        partial void OnPreviewZoomChanged(double value)
        {
            if (value < 0.25)
            {
                PreviewZoom = 0.25;
                return;
            }

            if (value > 3.0)
            {
                PreviewZoom = 3.0;
                return;
            }

            OnPropertyChanged(nameof(PreviewZoomPercent));
        }

        partial void OnIsProjectRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanRunProject));
            OnPropertyChanged(nameof(CanStopProject));
            RunProjectCommand.NotifyCanExecuteChanged();
            StopProjectCommand.NotifyCanExecuteChanged();
        }
        
        // --- Основная логика синхронизации и исправления ошибки ---

        private void UpdateTreeAndUIFromText()
        {
            try
            {
                ErrorMessage = "";
                var rootJson = JObject.Parse(JsonText);
                
                // Это первый запуск, просто строим и обновляем UI
                ControlTreeRoot = TreeBuilder.BuildTree(rootJson);

                UpdateUIFromRootModel();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"JSON Parse Error: {ex.Message}";
                ControlTreeRoot = null;
                CurrentDocument = null;
            }
        }

        private void UpdateUIFromRootModel(bool rebuildTree = true)
        {
            if (ControlTreeRoot?.ParentJsonContainer == null) return;
            
            // 1. СОХРАНЯЕМ JSON-ссылку текущего выделенного узла
            JObject? selectedJson = SelectedNode?.JsonNode;
            
            try
            {
                var rootJson = (JObject)ControlTreeRoot.ParentJsonContainer;
                var rootModel = ArxuiSerializer.Deserialize(rootJson.ToString());
                
                if (rootModel == null) return;

                PreviewSurfaceWidth = rootModel.Design?.SurfaceWidth ?? 1280;
                PreviewSurfaceHeight = rootModel.Design?.SurfaceHeight ?? 800;
                CurrentDocument = rootModel;
                _uiNodesByPath = BuildUiNodeIndex(rootModel.Root);

                // 2. Обновляем JsonText (СИНХРОНИЗАЦИЯ)
                _suppressJsonTextChanged = true;
                try
                {
                    JsonText = ArxuiSerializer.Serialize(rootModel);
                    if (!string.IsNullOrWhiteSpace(_currentDocumentPath))
                    {
                        File.WriteAllText(_currentDocumentPath!, JsonText);
                    }
                }
                finally
                {
                    _suppressJsonTextChanged = false;
                }

                // 3. Строим UI (Рендеринг)
                RenderedContent = UiBuilder.Build(rootModel.Root, LoadedProject);
                
                if (!rebuildTree)
                {
                    return;
                }

                // 4. ПЕРЕСТРАИВАЕМ ДЕРЕВО: Это создает НОВЫЕ ControlNode объекты
                ControlTreeRoot = TreeBuilder.BuildTree(rootJson);
                _controlNodesByPath = BuildControlNodeIndex(ControlTreeRoot);
                
                // 5. ВОССТАНОВЛЕНИЕ: Ищем предыдущий выделенный JSON-объект в новом дереве
                if (selectedJson != null)
                {
                    var newlySelectedNode = FindNodeByJson(ControlTreeRoot, selectedJson);
                    
                    if (newlySelectedNode != null)
                    {
                        SelectedNode = newlySelectedNode;
                        // Принудительно уведомляем View о том, что SelectedNode изменился,
                        // чтобы TreeView обновил выделение
                        OnPropertyChanged(nameof(SelectedNode));
                    }
                }
                
                // 6. Перезагружаем свойства для текущего узла (который теперь восстановлен)
                LoadProperties(SelectedNode);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Rendering Error: {ex.Message}";
            }
        }

        // Вспомогательный метод: ищет узел в дереве по ссылке на исходный JObject
        private ControlNode? FindNodeByJson(ControlNode? root, JObject targetJson)
        {
            if (root == null) return null;

            // Сравнение по ссылке (так как JObject — это объект-ссылка)
            if (root.JsonNode == targetJson)
            {
                return root;
            }

            // Рекурсивный поиск
            foreach (var child in root.Children)
            {
                var found = FindNodeByJson(child, targetJson);
                if (found != null)
                {
                    return found;
                }
            }
            
            return null;
        }


        private void LoadProperties(ControlNode? node)
        {
            EditableProperties.Clear();

            if (node?.JsonNode.TryGetValue("Properties", out JToken? propertiesToken) == true && propertiesToken is JObject propertiesObject)
            {
                foreach (var prop in propertiesObject.Properties())
                {
                    if (prop.Value is JValue jValue)
                    {
                        var item = new PropertyItem(
                            prop.Name, 
                            jValue, 
                            propertiesObject
                        );
                        EditableProperties.Add(item);
                    }
                }
            }
        }

        private void OnDesignerSelectionChanged(object? sender, UiNode? node)
        {
            if (_selectionSyncInProgress)
            {
                return;
            }

            try
            {
                _selectionSyncInProgress = true;

                if (node == null)
                {
                    SelectedNode = null;
                    return;
                }

                var path = FindUiNodePath(node);
                if (path != null && _controlNodesByPath.TryGetValue(path, out var controlNode))
                {
                    SelectedNode = controlNode;
                }
            }
            finally
            {
                _selectionSyncInProgress = false;
            }
        }

        private void SyncPreviewSelectionFromOutline(ControlNode? node)
        {
            if (_selectionSyncInProgress)
            {
                return;
            }

            try
            {
                _selectionSyncInProgress = true;

                if (node == null)
                {
                    DesignerSelectionService.Select(null);
                    return;
                }

                if (_uiNodesByPath.TryGetValue(node.NodePath, out var uiNode))
                {
                    DesignerSelectionService.Select(uiNode);
                }
                else
                {
                    DesignerSelectionService.Select(null);
                }
            }
            finally
            {
                _selectionSyncInProgress = false;
            }
        }

        private string? FindUiNodePath(UiNode target)
        {
            foreach (var pair in _uiNodesByPath)
            {
                if (ReferenceEquals(pair.Value, target))
                {
                    return pair.Key;
                }
            }

            return null;
        }

        private static Dictionary<string, UiNode> BuildUiNodeIndex(UiNode root)
        {
            var index = new Dictionary<string, UiNode>();
            VisitUiNode(root, "", index);
            return index;
        }

        private static void VisitUiNode(UiNode node, string currentPath, IDictionary<string, UiNode> index)
        {
            index[currentPath] = node;

            foreach (var property in node.Properties)
            {
                if (property.Value is NodeValue nodeValue)
                {
                    VisitUiNode(nodeValue.Node, AppendPath(currentPath, property.Key), index);
                }
                else if (property.Value is CollectionValue collectionValue)
                {
                    for (var i = 0; i < collectionValue.Items.Count; i++)
                    {
                        if (collectionValue.Items[i] is NodeValue collectionNodeValue)
                        {
                            VisitUiNode(collectionNodeValue.Node, $"{AppendPath(currentPath, property.Key)}[{i}]", index);
                        }
                    }
                }
            }
        }

        private static Dictionary<string, ControlNode> BuildControlNodeIndex(ControlNode? root)
        {
            var index = new Dictionary<string, ControlNode>();
            if (root == null)
            {
                return index;
            }

            VisitControlNode(root, index);
            return index;
        }

        private static void VisitControlNode(ControlNode node, IDictionary<string, ControlNode> index)
        {
            if (!string.IsNullOrWhiteSpace(node.NodePath) && node.JsonNode["TypeName"] != null)
            {
                index[node.NodePath] = node;
            }

            foreach (var child in node.Children)
            {
                VisitControlNode(child, index);
            }
        }

        private static string AppendPath(string parentPath, string segment)
        {
            return string.IsNullOrWhiteSpace(parentPath) ? segment : $"{parentPath}/{segment}";
        }
        
        // --- Команды конструктора ---

        [RelayCommand]
        private void LoadProject()
        {
            try
            {
                ErrorMessage = "";
                var project = _projectDiscoveryService.Load(ProjectPathInput);
                LoadedProject = project;

                ProjectArxuiFiles.Clear();
                foreach (var file in project.ArxuiFiles)
                {
                    ProjectArxuiFiles.Add(file);
                }

                ProjectSummary =
                    $"Project: {Path.GetFileName(project.ProjectPath)} | Assembly: {project.AssemblyName} | TFM: {project.TargetFramework} | .arxui: {project.ArxuiFiles.Count} | .axaml: {project.AxamlFiles.Count}";

                if (ProjectArxuiFiles.Count > 0)
                {
                    var startupFile = SelectStartupDocument(ProjectArxuiFiles) ?? ProjectArxuiFiles[0];
                    SelectedProjectFile = startupFile;
                    OpenProjectFile(startupFile);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Project Load Error: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanOpenSelectedProjectFile))]
        private void OpenSelectedProjectFile()
        {
            if (SelectedProjectFile != null)
            {
                OpenProjectFile(SelectedProjectFile);
            }
        }

        private void OpenProjectFile(ProjectFileItem file)
        {
            try
            {
                ErrorMessage = "";
                _currentDocumentPath = file.FullPath;

                _suppressJsonTextChanged = true;
                try
                {
                    JsonText = File.ReadAllText(file.FullPath);
                }
                finally
                {
                    _suppressJsonTextChanged = false;
                }

                UpdateTreeAndUIFromText();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Open File Error: {ex.Message}";
            }
        }

        public bool CanOpenSelectedProjectFile => SelectedProjectFile != null;

        private static ProjectFileItem? SelectStartupDocument(IEnumerable<ProjectFileItem> files)
        {
            foreach (var file in files)
            {
                var kind = TryReadDocumentKind(file.FullPath);
                if (kind == UiDocumentKind.Window || kind == UiDocumentKind.Control)
                {
                    return file;
                }
            }

            return files.FirstOrDefault();
        }

        private static UiDocumentKind? TryReadDocumentKind(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var json = JObject.Parse(reader.ReadToEnd());
                return json["Kind"]?.ToObject<UiDocumentKind>();
            }
            catch
            {
                return null;
            }
        }

        [RelayCommand]
        private void SetWorkspaceMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return;
            }

            WorkspaceMode = mode;
        }

        [RelayCommand]
        private void ZoomIn()
        {
            PreviewZoom = Math.Round(PreviewZoom + 0.1, 2);
        }

        [RelayCommand]
        private void ZoomOut()
        {
            PreviewZoom = Math.Round(PreviewZoom - 0.1, 2);
        }

        [RelayCommand]
        private void ResetZoom()
        {
            PreviewZoom = 1.0;
        }

        [RelayCommand(CanExecute = nameof(CanRunProject))]
        private async Task RunProject()
        {
            if (IsProjectRunning || string.IsNullOrWhiteSpace(ProjectPathInput))
            {
                return;
            }

            try
            {
                if (LoadedProject == null || !string.Equals(LoadedProject.ProjectPath, ProjectPathInput, StringComparison.OrdinalIgnoreCase))
                {
                    var project = _projectDiscoveryService.Load(ProjectPathInput);
                    LoadedProject = project;
                }

                if (LoadedProject == null)
                {
                    RunStatus = "Project is not loaded.";
                    return;
                }

                var projectPath = LoadedProject.ProjectPath;
                var workingDirectory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory;

                RunOutput = "";
                RunStatus = $"Starting {Path.GetFileName(projectPath)}...";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"run --project \"{projectPath}\"",
                        WorkingDirectory = workingDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                process.OutputDataReceived += (_, args) => AppendRunOutput(args.Data);
                process.ErrorDataReceived += (_, args) => AppendRunOutput(args.Data);

                if (!process.Start())
                {
                    RunStatus = "Failed to start process.";
                    return;
                }

                _runningProcess = process;
                IsProjectRunning = true;
                RunStatus = $"Running PID {process.Id}";
                AppendRunOutput($"> dotnet run --project \"{projectPath}\"");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                var exitCode = process.ExitCode;
                _runningProcess = null;
                IsProjectRunning = false;
                RunStatus = $"Process finished with exit code {exitCode}.";
                AppendRunOutput($"Process finished with exit code {exitCode}.");
            }
            catch (Exception ex)
            {
                _runningProcess = null;
                IsProjectRunning = false;
                RunStatus = "Run failed.";
                AppendRunOutput($"Run error: {ex.Message}");
                ErrorMessage = $"Run Error: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanStopProject))]
        private void StopProject()
        {
            if (_runningProcess == null || _runningProcess.HasExited)
            {
                return;
            }

            try
            {
                AppendRunOutput($"Stopping PID {_runningProcess.Id}...");
                _runningProcess.Kill(entireProcessTree: true);
                RunStatus = "Stopping process...";
            }
            catch (Exception ex)
            {
                AppendRunOutput($"Stop error: {ex.Message}");
                ErrorMessage = $"Stop Error: {ex.Message}";
            }
        }

        [RelayCommand]
        private void AddNewControl(JObject? controlTemplate)
        {
            if (SelectedNode == null)
            {
                ErrorMessage = "Please select a container control (e.g., StackPanel, Grid) first.";
                return;
            }
            
            if (controlTemplate == null) return;

            JObject newControlJson = (JObject)controlTemplate.DeepClone();

            if (!SelectedNode.JsonNode.TryGetValue("Properties", out JToken? propertiesToken) || propertiesToken is not JObject propertiesObject)
            {
                ErrorMessage = $"Selected node {SelectedNode.DisplayName} does not have a 'Properties' object.";
                return;
            }

            JArray? childrenArray = propertiesObject.Properties()
                                                  .Where(p => p.Value is JArray)
                                                  .Select(p => p.Value as JArray)
                                                  .FirstOrDefault();

            if (childrenArray == null)
            {
                ErrorMessage = $"Selected node '{SelectedNode.DisplayName}' does not have a suitable JArray (e.g., 'Children') property to add controls.";
                return;
            }

            childrenArray.Add(newControlJson);
            DesignerService.Instance.NotifyJsonChanged(DesignerChangeKind.Structure);
            ErrorMessage = "";
        }

        [RelayCommand(CanExecute = nameof(CanDeleteSelectedControl))]
        private void DeleteSelectedControl()
        {
            if (SelectedNode == null || SelectedNode.ParentJsonContainer == null) return;

            if (SelectedNode.ParentJsonContainer is JArray parentArray)
            {
                parentArray.Remove(SelectedNode.JsonNode);
            }
            else if (SelectedNode.ParentJsonContainer is JObject parentObject)
            {
                var propertyToRemove = parentObject.Properties()
                                                 .FirstOrDefault(p => p.Value == SelectedNode.JsonNode);
                
                propertyToRemove?.Remove();
            }

            SelectedNode = null;
            DesignerService.Instance.NotifyJsonChanged(DesignerChangeKind.Structure);
            ErrorMessage = "";
        }

        public bool CanDeleteSelectedControl => SelectedNode != null && 
                                               SelectedNode.ParentJsonContainer != null && 
                                               SelectedNode.DisplayName != "Root Document";

        private void AppendRunOutput(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                RunOutput = string.IsNullOrWhiteSpace(RunOutput)
                    ? line
                    : $"{RunOutput}{Environment.NewLine}{line}";
            });
        }
    }
}
