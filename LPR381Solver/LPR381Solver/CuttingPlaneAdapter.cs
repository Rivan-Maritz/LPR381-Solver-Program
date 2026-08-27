using System;
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
        // variable can map to one or two canonical columns (e.g. unrestricted variables
        // get split into a "_pos" and "_neg" column), so we mark every column that
        // mapping points to.
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