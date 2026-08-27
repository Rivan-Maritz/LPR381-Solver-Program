using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LPR381Solver
{
    public sealed class CuttingPlaneAdapter : IModelSolver
    {
        public string Name => AlgorithmNames.CuttingPlane;

        public SolverExecution Solve(LinearProgrammingModel model, CanonicalForm canonicalForm)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (canonicalForm == null)
                throw new ArgumentNullException(nameof(canonicalForm));

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

            string[] relations = canonicalForm.NormalizedRelations
                .Select(RelationToString)
                .ToArray();
            string[] signRestrictions = GetSignRestrictions(canonicalForm);
            bool isMax = model.ObjectiveSense == ObjectiveSense.Maximize;

            // Binary variables need an upper bound of 1, which nothing else
            // enforces on its own - without this, Cutting Plane could accept an
            // integer answer above 1 as "valid" for a bin-restricted variable.
            var extraRows = new List<double[]>();
            var extraRhs = new List<double>();
            var extraRelations = new List<string>();
            for (int col = 0; col < signRestrictions.Length; col++)
            {
                if (signRestrictions[col] == "bin")
                {
                    var row = new double[columns];
                    row[col] = 1;
                    extraRows.Add(row);
                    extraRhs.Add(1);
                    extraRelations.Add("<=");
                }
            }

            if (extraRows.Count > 0)
            {
                int newRowCount = rows + extraRows.Count;
                var newA = new double[newRowCount, columns];
                var newB = new double[newRowCount];
                var newRelations = new string[newRowCount];

                for (int r = 0; r < rows; r++)
                {
                    for (int col = 0; col < columns; col++)
                        newA[r, col] = a[r, col];
                    newB[r] = b[r];
                    newRelations[r] = relations[r];
                }
                for (int i = 0; i < extraRows.Count; i++)
                {
                    for (int col = 0; col < columns; col++)
                        newA[rows + i, col] = extraRows[i][col];
                    newB[rows + i] = extraRhs[i];
                    newRelations[rows + i] = extraRelations[i];
                }

                a = newA;
                b = newB;
                relations = newRelations;
                rows = newRowCount;
            }

            string cutLog;
            SimplexResult result;
            using (var writer = new StringWriter())
            {
                CuttingPlane.Solve(c, a, b, relations, signRestrictions, isMax, writer, out result);
                cutLog = writer.ToString();
            }

            if (result == null)
            {
                throw new InfeasibleModelException(
                    "Cutting Plane could not find a feasible integer solution for this model.");
            }

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

            foreach (string block in cutLog.Split(new[] { "===" }, StringSplitOptions.RemoveEmptyEntries))
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

        private static string RelationToString(ConstraintRelation relation)
        {
            switch (relation)
            {
                case ConstraintRelation.LessThanOrEqual: return "<=";
                case ConstraintRelation.GreaterThanOrEqual: return ">=";
                case ConstraintRelation.Equal: return "=";
                default: throw new ArgumentOutOfRangeException(nameof(relation));
            }
        }

        // Integer/binary restrictions live on canonicalForm.OriginalVariableRestrictions,
        // indexed by the ORIGINAL variable, not the canonical column. A single original
        // variable can map to one or two canonical columns, so we mark every column
        // that mapping points to.
        private static string[] GetSignRestrictions(CanonicalForm canonicalForm)
        {
            var restrictions = new string[canonicalForm.DecisionVariableCount];
            for (int i = 0; i < restrictions.Length; i++)
                restrictions[i] = "+";

            foreach (OriginalVariableMapping mapping in canonicalForm.OriginalVariableMappings)
            {
                VariableRestriction original = canonicalForm.OriginalVariableRestrictions[mapping.OriginalVariableIndex];
                string token = original == VariableRestriction.Integer ? "int"
                    : original == VariableRestriction.Binary ? "bin"
                    : null;

                if (token == null) continue;

                foreach (int canonicalIndex in mapping.CanonicalIndexes)
                    if (canonicalIndex < restrictions.Length)
                        restrictions[canonicalIndex] = token;
            }

            return restrictions;
        }
    }
}