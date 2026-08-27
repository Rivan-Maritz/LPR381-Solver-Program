// Member 1 contribution
// Contributor: Dewald Allers
// Scope: Shared algorithm contract, selection catalogue, and Primal Simplex adapter.

using System;
using System.Collections.Generic;
using System.Linq;

namespace LPR381Solver
{
    public interface IModelSolver
    {
        string Name { get; }
        SolverExecution Solve(LinearProgrammingModel model, CanonicalForm canonicalForm);
    }

    public sealed class SolverExecution
    {
        public SolverRunReport Report { get; set; }
        public SimplexResult SimplexResult { get; set; }
    }

    public static class AlgorithmNames
    {
        public const string PrimalSimplex = "Primal Simplex";
        public const string RevisedPrimalSimplex = "Revised Primal Simplex";
        public const string BranchAndBoundSimplex = "Branch & Bound Simplex";
        public const string CuttingPlane = "Cutting Plane";
        public const string BranchAndBoundKnapsack = "Branch & Bound Knapsack";

        public static readonly string[] All =
        {
            PrimalSimplex,
            RevisedPrimalSimplex,
            BranchAndBoundSimplex,
            CuttingPlane,
            BranchAndBoundKnapsack
        };
    }

    public sealed class SolverRegistry
    {
        private readonly Dictionary<string, IModelSolver> solvers;

        public SolverRegistry(IEnumerable<IModelSolver> availableSolvers)
        {
            solvers = (availableSolvers ?? Enumerable.Empty<IModelSolver>())
                .ToDictionary(solver => solver.Name, StringComparer.OrdinalIgnoreCase);
        }

        public static SolverRegistry CreateDefault()
        {
            return new SolverRegistry(new IModelSolver[]
            {
        new PrimalSimplexAdapter(),
        new RevisedPrimalSimplexAdapter(),
        new CuttingPlaneAdapter()
            });
        }

        public SolverExecution Solve(string algorithmName, LinearProgrammingModel model, CanonicalForm canonicalForm)
        {
            if (string.IsNullOrWhiteSpace(algorithmName))
                throw new AlgorithmUnavailableException("Select an algorithm first.");

            IModelSolver solver;
            if (!solvers.TryGetValue(algorithmName, out solver))
            {
                throw new AlgorithmUnavailableException(
                    $"{algorithmName} has not yet been connected by its assigned team member. " +
                    "The input model and canonical form are valid and ready for that solver.");
            }

            return solver.Solve(model, canonicalForm);
        }
    }

    public sealed class PrimalSimplexAdapter : IModelSolver
    {
        public string Name => AlgorithmNames.PrimalSimplex;

        public SolverExecution Solve(LinearProgrammingModel model, CanonicalForm canonicalForm)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (canonicalForm == null)
                throw new ArgumentNullException(nameof(canonicalForm));

            if (canonicalForm.NormalizedRelations.Any(relation => relation != ConstraintRelation.LessThanOrEqual))
            {
                throw new AlgorithmCompatibilityException(
                    "The current Primal Simplex implementation requires <= constraints with a directly feasible slack basis. " +
                    "Choose a two-phase/revised solver after that module is connected.");
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

            foreach (double[,] tableau in result.Iterations)
                report.Iterations.Add(PrimalSimplex.TableauToString(tableau));

            return new SolverExecution
            {
                Report = report,
                SimplexResult = result
            };
        }
    }
}
