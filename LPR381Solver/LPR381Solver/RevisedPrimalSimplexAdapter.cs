using System;
using System.IO;
using System.Linq;

namespace LPR381Solver
{
    public sealed class RevisedPrimalSimplexAdapter : IModelSolver
    {
        public string Name => AlgorithmNames.RevisedPrimalSimplex;

        public SolverExecution Solve(LinearProgrammingModel model, CanonicalForm canonicalForm)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (canonicalForm == null)
                throw new ArgumentNullException(nameof(canonicalForm));

            // RevisedPrimalSimplex.cs only handles <= constraints right now (see the
            // note at the top of that file), same restriction as PrimalSimplexAdapter.
            if (canonicalForm.NormalizedRelations.Any(relation => relation != ConstraintRelation.LessThanOrEqual))
            {
                throw new AlgorithmCompatibilityException(
                    "The current Revised Primal Simplex implementation only handles <= constraints. " +
                    "Choose a different solver for models with >= or = constraints.");
            }

            int rows = canonicalForm.RightHandSides.Length;
            int columns = canonicalForm.DecisionVariableCount;
            var a = new double[rows, columns];
            var c = new double[columns];
            var b = (double[])canonicalForm.RightHandSides.Clone();

            for (int column = 0; column < columns; column++)
            {
                c[column] = canonicalForm.ObjectiveCoefficients[column];
                for (int row = 0; row < rows; row++)
                    a[row, column] = canonicalForm.ConstraintMatrix[row, column];
            }

            bool isMax = model.ObjectiveSense == ObjectiveSense.Maximize;

            // RevisedPrimalSimplex.cs writes its product-form / price-out log as text
            // rather than building a big tableau, so we capture that text for the
            // report, then separately run PrimalSimplex.Solve on the same data to get
            // a SimplexResult - this is what the grids in MainForm (tableau, B-inverse,
            // shadow prices) actually read from, and both solvers reach the same
            // optimal answer since the underlying LP is identical.
            string productFormLog;
            using (var writer = new StringWriter())
            {
                RevisedPrimalSimplex.Solve(c, a, b, isMax, writer);
                productFormLog = writer.ToString();
            }

            SimplexResult result = PrimalSimplex.Solve(c, a, b);
            result.DecisionVariableNames = canonicalForm.Variables
                .Take(canonicalForm.DecisionVariableCount)
                .Select(variable => variable.Name)
                .ToArray();
            result.OriginalObjectiveValueMultiplier = canonicalForm.OriginalObjectiveValueMultiplier;

            double displayedOptimalValue = result.GetOriginalOptimalValue();
            var report = new SolverRunReport(Name)
            {
                Status = "Optimal",
                Summary = "Optimal objective value: " + CanonicalFormFormatter.FormatNumber(displayedOptimalValue)
            };

            // Show the product-form / price-out log as its own iteration entries,
            // one block per "===" section, so it reads the same as the other
            // algorithms' iteration lists in the UI.
            foreach (string block in productFormLog.Split(new[] { "===" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = block.Trim();
                if (trimmed.Length > 0)
                    report.Iterations.Add(trimmed);
            }

            return new SolverExecution
            {
                Report = report,
                SimplexResult = result
            };
        }
    }
}