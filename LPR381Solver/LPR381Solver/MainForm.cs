// Member 1 contribution
// Contributor: Dewald Allers
// Scope: Menu-driven WinForms UI, workflow orchestration, and user-facing error handling.

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LPR381Solver
{
    public class MainForm : Form
    {
        private readonly SolverRegistry solverRegistry;

        private TextBox inputPathTextBox;
        private ComboBox algorithmSelector;
        private Button solveButton;
        private Button exportButton;
        private RichTextBox modelTextBox;
        private ComboBox iterationSelector;
        private DataGridView tableauGrid;
        private DataGridView bInverseGrid;
        private DataGridView shadowPriceGrid;
        private Label optimalValueLabel;
        private ToolStripStatusLabel statusLabel;
        private TabControl tabs;

        private string currentInputPath;
        private LinearProgrammingModel currentModel;
        private CanonicalForm currentCanonicalForm;
        private SolverRunReport currentReport;
        private SimplexResult currentResult;

        private DataGridView variableRangeGrid;
        private DataGridView rhsRangeGrid;
        private DataGridView dualityGrid;

        private AnalyzeSensitivity sensitivityAnalyzer;

        public MainForm()
        {
            solverRegistry = SolverRegistry.CreateDefault();
            InitializeUserInterface();
        }

        private void InitializeUserInterface()
        {
            Text = "LPR381 Solver - Member 1 Core & Interface";
            Width = 1180;
            Height = 800;
            MinimumSize = new Size(1100, 650);
            StartPosition = FormStartPosition.CenterScreen;

            Controls.Add(BuildTabs());
            Controls.Add(BuildWorkflowPanel());
            MenuStrip menu = BuildMenu();
            Controls.Add(menu);
            Controls.Add(BuildStatusStrip());
            MainMenuStrip = menu;
            SetWorkflowState(false, false);
        }

        private MenuStrip BuildMenu()
        {
            var menu = new MenuStrip();
            var fileMenu = new ToolStripMenuItem("&File");
            fileMenu.DropDownItems.Add("&Open input file...", null, (sender, args) => OpenInputFile());
            fileMenu.DropDownItems.Add("&Export output file...", null, (sender, args) => ExportOutputFile());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("E&xit", null, (sender, args) => Close());

            var modelMenu = new ToolStripMenuItem("&Model");
            modelMenu.DropDownItems.Add("Show &canonical form", null, (sender, args) => ShowCanonicalForm());

            var solveMenu = new ToolStripMenuItem("&Solve");
            foreach (string algorithm in AlgorithmNames.All)
            {
                string selectedAlgorithm = algorithm;
                solveMenu.DropDownItems.Add(selectedAlgorithm, null, (sender, args) =>
                {
                    algorithmSelector.SelectedItem = selectedAlgorithm;
                    RunSelectedAlgorithm();
                });
            }

            var helpMenu = new ToolStripMenuItem("&Help");
            helpMenu.DropDownItems.Add("Input file &format", null, (sender, args) => ShowInputFormatHelp());
            helpMenu.DropDownItems.Add("&About", null, (sender, args) => MessageBox.Show(
                "LPR381 Linear & Integer Programming Solver\n\nMember 1 - Core & Interface\nContributor: Dewald Allers",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information));

            menu.Items.Add(fileMenu);
            menu.Items.Add(modelMenu);
            menu.Items.Add(solveMenu);
            menu.Items.Add(helpMenu);
            return menu;
        }

        private Control BuildWorkflowPanel()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(10) };
            panel.Controls.Add(new Label { Text = "Input model:", Left = 10, Top = 14, Width = 85 });

            inputPathTextBox = new TextBox
            {
                Left = 100, Top = 10, Width = 790, ReadOnly = true
            };
            var openButton = new Button
            {
                Text = "Browse...", Left = 900, Top = 8, Width = 105
            };
            openButton.Click += (sender, args) => OpenInputFile();

            panel.Controls.Add(new Label { Text = "Algorithm:", Left = 10, Top = 56, Width = 85 });
            algorithmSelector = new ComboBox
            {
                Left = 100, Top = 52, Width = 260,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            algorithmSelector.Items.AddRange(AlgorithmNames.All.Cast<object>().ToArray());
            algorithmSelector.SelectedIndex = 0;

            solveButton = new Button { Text = "Solve", Left = 375, Top = 50, Width = 120 };
            solveButton.Click += (sender, args) => RunSelectedAlgorithm();
            exportButton = new Button { Text = "Export results...", Left = 510, Top = 50, Width = 145 };
            exportButton.Click += (sender, args) => ExportOutputFile();

            var contributorLabel = new Label
            {
                Text = "Member 1: Dewald Allers", Left = 690, Top = 57, Width = 315,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            panel.Controls.Add(inputPathTextBox);
            panel.Controls.Add(openButton);
            panel.Controls.Add(algorithmSelector);
            panel.Controls.Add(solveButton);
            panel.Controls.Add(exportButton);
            panel.Controls.Add(contributorLabel);
            return panel;
        }

        private Control BuildTabs()
        {
            tabs = new TabControl { Dock = DockStyle.Fill };
            var modelPage = new TabPage("Model & Canonical Form");
            modelTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false,
                BackColor = Color.White, Font = new Font("Consolas", 10F),
                Text = "Open an LPR381 input text file to validate and display its canonical form."
            };
            modelPage.Controls.Add(modelTextBox);

            var iterationPage = new TabPage("Algorithm Iterations");
            var iterationTopPanel = new Panel { Dock = DockStyle.Top, Height = 45 };
            iterationTopPanel.Controls.Add(new Label { Text = "Iteration:", Left = 10, Top = 14, Width = 65 });
            iterationSelector = new ComboBox
            {
                Left = 80, Top = 10, Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false
            };
            iterationSelector.SelectedIndexChanged += IterationSelector_SelectedIndexChanged;
            iterationTopPanel.Controls.Add(iterationSelector);
            tableauGrid = CreateReadOnlyGrid();
            tableauGrid.RowHeadersVisible = true;
            tableauGrid.RowHeadersWidth = 70;
            iterationPage.Controls.Add(tableauGrid);
            iterationPage.Controls.Add(iterationTopPanel);

            var sensitivityPage = new TabPage("Sensitivity Analysis");

            var sensitivityLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(10)
            };

            sensitivityLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50F));

            sensitivityLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50F));

            sensitivityLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 30F));

            sensitivityLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 25F));

            sensitivityLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 30F));

            sensitivityLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 25F));

            sensitivityLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 25F));

            sensitivityLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 40F));

            bInverseGrid = CreateReadOnlyGrid();
            shadowPriceGrid = CreateReadOnlyGrid();

            variableRangeGrid = CreateReadOnlyGrid();
            rhsRangeGrid = CreateReadOnlyGrid();

            dualityGrid = CreateReadOnlyGrid();

            sensitivityLayout.Controls.Add(
                new Label
                {
                    Text = "B-Inverse",
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold)
                }, 0, 0);

            sensitivityLayout.Controls.Add(
                new Label
                {
                    Text = "Shadow Prices",
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold)
                }, 1, 0);

            sensitivityLayout.Controls.Add(
                bInverseGrid,
                0,
                1);

            sensitivityLayout.Controls.Add(
                shadowPriceGrid,
                1,
                1);

            sensitivityLayout.Controls.Add(
                new Label
                {
                    Text = "Variable Sensitivity",
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold)
                }, 0, 2);

            sensitivityLayout.Controls.Add(
                new Label
                {
                    Text = "RHS Sensitivity",
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold)
                }, 1, 2);

            sensitivityLayout.Controls.Add(
                variableRangeGrid,
                0,
                3);

            sensitivityLayout.Controls.Add(
                rhsRangeGrid,
                1,
                3);

            dualityGrid.Columns.Add("item", "Item");
            dualityGrid.Columns.Add("value", "Value");

            sensitivityLayout.Controls.Add(
                dualityGrid,
                0,
                4);

            sensitivityLayout.SetColumnSpan(
                dualityGrid,
                2);

            optimalValueLabel = new Label
            {
                Text = "Optimal Value: -",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            sensitivityLayout.Controls.Add(
                optimalValueLabel,
                0,
                5);

            sensitivityLayout.SetColumnSpan(
                optimalValueLabel,
                2);

            sensitivityPage.Controls.Add(
                sensitivityLayout);

            tabs.TabPages.Add(modelPage);
            tabs.TabPages.Add(iterationPage);
            tabs.TabPages.Add(sensitivityPage);
            return tabs;
        }

        private StatusStrip BuildStatusStrip()
        {
            var strip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Ready. Open an input model.");
            strip.Items.Add(statusLabel);
            return strip;
        }

        private static DataGridView CreateReadOnlyGrid()
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
        }

        private void OpenInputFile()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Select an LPR381 model input file";
                dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadModel(dialog.FileName);
            }
        }

        internal void LoadModel(string path)
        {
            try
            {
                LinearProgrammingModel parsedModel = ModelInputParser.ParseFile(path);
                CanonicalForm canonicalForm = CanonicalFormConverter.Convert(parsedModel);
                currentInputPath = path;
                currentModel = parsedModel;
                currentCanonicalForm = canonicalForm;
                currentReport = null;
                currentResult = null;
                inputPathTextBox.Text = path;
                modelTextBox.Text = ModelFormatter.Format(currentModel) + Environment.NewLine + CanonicalFormFormatter.Format(currentCanonicalForm);
                ResetResultViews();
                SetWorkflowState(true, false);
                tabs.SelectedIndex = 0;
                statusLabel.Text = $"Loaded {currentModel.VariableCount} variables and {currentModel.ConstraintCount} constraints.";
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
        }

        private void ShowCanonicalForm()
        {
            if (currentCanonicalForm == null)
            {
                MessageBox.Show("Open a valid input file first.", "No model loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            tabs.SelectedIndex = 0;
            modelTextBox.Focus();
        }

        private void RunSelectedAlgorithm()
        {
            if (currentModel == null || currentCanonicalForm == null)
            {
                MessageBox.Show("Open and validate an input file before solving.", "No model loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                string algorithm = Convert.ToString(algorithmSelector.SelectedItem);
                statusLabel.Text = "Solving with " + algorithm + "...";
                Cursor = Cursors.WaitCursor;
                SolverExecution execution = solverRegistry.Solve(algorithm, currentModel, currentCanonicalForm);
                currentReport = execution.Report;
                currentResult = execution.SimplexResult;
                PopulateResultViews();
                SetWorkflowState(true, true);
                tabs.SelectedIndex = 1;
                statusLabel.Text = algorithm + " completed successfully. Export the result when ready.";
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void ExportOutputFile()
        {
            if (currentModel == null || currentCanonicalForm == null)
            {
                MessageBox.Show("Open a valid input file before exporting.", "Nothing to export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Export LPR381 solver output";
                dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.DefaultExt = "txt";
                dialog.AddExtension = true;
                string baseName = string.IsNullOrWhiteSpace(currentInputPath)
                    ? "solver-output"
                    : Path.GetFileNameWithoutExtension(currentInputPath) + "-output";
                dialog.FileName = baseName + ".txt";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    OutputFileWriter.Write(dialog.FileName, currentInputPath, currentModel, currentCanonicalForm, currentReport);
                    statusLabel.Text = "Output written to " + dialog.FileName;
                    MessageBox.Show("The canonical form and available algorithm results were exported successfully.", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    HandleError(ex);
                }
            }
        }

        private void PopulateResultViews()
        {
            ResetResultViews();
            if (currentResult == null)
                return;

            for (int i = 0; i < currentResult.Iterations.Count; i++)
                iterationSelector.Items.Add($"Iteration {i}");
            iterationSelector.Enabled = currentResult.Iterations.Count > 0;
            if (currentResult.Iterations.Count > 0)
                iterationSelector.SelectedIndex = currentResult.Iterations.Count - 1;
            PopulateBInverseGrid();
            PopulateShadowPriceGrid();

            sensitivityAnalyzer =
                new AnalyzeSensitivity
                {
                    OriginalModel = currentModel
                };

            sensitivityAnalyzer.SolveOriginal();

            PopulateVariableSensitivity();
            PopulateRHSSensitivity();
            PopulateDuality();

            optimalValueLabel.Text =
                "Optimal value: " +
                CanonicalFormFormatter.FormatNumber(
                    currentResult.GetOriginalOptimalValue());
        }

        private void ResetResultViews()
        {
            iterationSelector.Items.Clear();
            iterationSelector.Enabled = false;
            tableauGrid.Columns.Clear();
            tableauGrid.Rows.Clear();
            bInverseGrid.Columns.Clear();
            bInverseGrid.Rows.Clear();
            shadowPriceGrid.Columns.Clear();
            shadowPriceGrid.Rows.Clear();

            variableRangeGrid?.Columns.Clear();
            variableRangeGrid?.Rows.Clear();

            rhsRangeGrid?.Columns.Clear();
            rhsRangeGrid?.Rows.Clear();

            dualityGrid?.Rows.Clear();

            optimalValueLabel.Text = "Optimal value: -";
        }

        private void IterationSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (currentResult != null && iterationSelector.SelectedIndex >= 0)
                PopulateTableauGrid(currentResult.Iterations[iterationSelector.SelectedIndex]);
        }

        private void PopulateTableauGrid(double[,] tableau)
        {
            tableauGrid.Columns.Clear();
            tableauGrid.Rows.Clear();
            int decisionCount = currentResult.NumOriginalVars;
            int constraintCount = currentResult.NumConstraints;
            int rows = tableau.GetLength(0);
            int columns = tableau.GetLength(1);
            string[] decisionNames = currentResult.DecisionVariableNames ??
                Enumerable.Range(1, decisionCount).Select(index => "x" + index).ToArray();

            for (int column = 0; column < columns; column++)
            {
                string header = column < decisionCount
                    ? decisionNames[column]
                    : column < decisionCount + constraintCount
                        ? $"s{column - decisionCount + 1}"
                        : "RHS";
                tableauGrid.Columns.Add("col" + column, header);
            }

            for (int row = 0; row < rows; row++)
            {
                var values = new object[columns];
                for (int column = 0; column < columns; column++)
                    values[column] = CanonicalFormFormatter.FormatNumber(tableau[row, column]);
                int rowIndex = tableauGrid.Rows.Add(values);
                tableauGrid.Rows[rowIndex].HeaderCell.Value = row == 0 ? "Z" : "C" + row;
            }
        }

        private void PopulateBInverseGrid()
        {
            double[,] inverse = currentResult.GetBInverse();
            int size = currentResult.NumConstraints;
            for (int column = 0; column < size; column++)
                bInverseGrid.Columns.Add("b" + column, "Column " + (column + 1));
            for (int row = 0; row < size; row++)
            {
                var values = new object[size];
                for (int column = 0; column < size; column++)
                    values[column] = CanonicalFormFormatter.FormatNumber(inverse[row, column]);
                bInverseGrid.Rows.Add(values);
            }
        }

        private void PopulateShadowPriceGrid()
        {
            shadowPriceGrid.Columns.Add("constraint", "Constraint");
            shadowPriceGrid.Columns.Add("price", "Shadow price");
            double[] prices = currentResult.GetShadowPrices();
            for (int i = 0; i < prices.Length; i++)
                shadowPriceGrid.Rows.Add("Constraint " + (i + 1), CanonicalFormFormatter.FormatNumber(prices[i]));
        }

        private void PopulateVariableSensitivity()
        {
            variableRangeGrid.Columns.Clear();
            variableRangeGrid.Rows.Clear();

            variableRangeGrid.Columns.Add(
                "Variable",
                "Variable");

            variableRangeGrid.Columns.Add(
                "Type",
                "Type");

            variableRangeGrid.Columns.Add(
                "Lower",
                "Lower Bound");

            variableRangeGrid.Columns.Add(
                "Upper",
                "Upper Bound");

            for (int i = 0;
                 i < currentModel.VariableCount;
                 i++)
            {
                try
                {
                    Range range;
                    string type;

                    try
                    {
                        range =
                            sensitivityAnalyzer
                                .GetBasicVariableRange(i);

                        type = "Basic";
                    }
                    catch
                    {
                        range =
                            sensitivityAnalyzer
                                .GetNonBasicVariableRange(i);

                        type = "Non-Basic";
                    }

                    variableRangeGrid.Rows.Add(
                        $"x{i + 1}",
                        type,
                        CanonicalFormFormatter.FormatNumber(
                            range.LowerBound),
                        CanonicalFormFormatter.FormatNumber(
                            range.UpperBound));
                }
                catch
                {
                }
            }
        }

        private void PopulateRHSSensitivity()
        {
            rhsRangeGrid.Columns.Clear();
            rhsRangeGrid.Rows.Clear();

            rhsRangeGrid.Columns.Add(
                "Constraint",
                "Constraint");

            rhsRangeGrid.Columns.Add(
                "Lower",
                "Lower Bound");

            rhsRangeGrid.Columns.Add(
                "Upper",
                "Upper Bound");

            for (int i = 0;
                 i < currentModel.ConstraintCount;
                 i++)
            {
                try
                {
                    Range range =
                        sensitivityAnalyzer
                            .GetRHSRange(i);

                    rhsRangeGrid.Rows.Add(
                        $"Constraint {i + 1}",
                        CanonicalFormFormatter.FormatNumber(
                            range.LowerBound),
                        CanonicalFormFormatter.FormatNumber(
                            range.UpperBound));
                }
                catch
                {
                }
            }
        }

        private void PopulateDuality()
        {
            dualityGrid.Rows.Clear();

            try
            {
                SensitivityResult dual =
                    sensitivityAnalyzer
                        .SolveDualModel();

                string strength =
                    sensitivityAnalyzer
                        .CheckDualityStrength();

                dualityGrid.Rows.Add(
                    "Duality Type",
                    strength);

                dualityGrid.Rows.Add(
                    "Primal Objective",
                    CanonicalFormFormatter.FormatNumber(
                        currentResult
                            .GetOriginalOptimalValue()));

                dualityGrid.Rows.Add(
                    "Dual Objective",
                    CanonicalFormFormatter.FormatNumber(
                        dual.ObjectiveValue));

                dualityGrid.Rows.Add(
                    "Dual Optimal",
                    dual.IsOptimal);
            }
            catch (Exception ex)
            {
                dualityGrid.Rows.Add(
                    "Duality Error",
                    ex.Message);
            }
        }

        private void SetWorkflowState(bool modelLoaded, bool solved)
        {
            solveButton.Enabled = modelLoaded;
            exportButton.Enabled = modelLoaded;
            iterationSelector.Enabled = solved && currentResult != null && currentResult.Iterations.Count > 0;
        }

        private void HandleError(Exception exception)
        {
            string title;
            MessageBoxIcon icon = MessageBoxIcon.Error;
            if (exception is ModelInputException || exception is ModelValidationException)
                title = "Invalid input model";
            else if (exception is AlgorithmCompatibilityException)
                title = "Algorithm cannot solve this model";
            else if (exception is AlgorithmUnavailableException)
            {
                title = "Algorithm module not connected";
                icon = MessageBoxIcon.Warning;
            }
            else if (exception is OutputWriteException)
                title = "Output file error";
            else if (exception is InfeasibleModelException)
                title = "Infeasible model";
            else if (exception is UnboundedModelException ||
                     (exception is InvalidOperationException && exception.Message.IndexOf("unbounded", StringComparison.OrdinalIgnoreCase) >= 0))
                title = "Unbounded model";
            else
                title = "Unexpected solver error";

            statusLabel.Text = title + ": " + exception.Message;
            MessageBox.Show(exception.Message, title, MessageBoxButtons.OK, icon);
        }

        private void ShowInputFormatHelp()
        {
            MessageBox.Show(
                "Objective line:\nmax +2 +3 +3\n\n" +
                "One line per constraint:\n+11 +8 +6 <= 40\n\n" +
                "Final variable restrictions:\nbin bin bin\n\n" +
                "Allowed restrictions: +, -, urs, int, bin",
                "LPR381 input format", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
