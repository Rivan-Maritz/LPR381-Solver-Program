using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LPR381Solver
{
    public static class BranchAndBoundSimplex
    {
        private const double eps = 1e-6;

        // Represents a node in the branch and bound search tree
        private class Node 
        {
            public List<double[]> A { get; set; }
            public List<double> B { get; set; }
            public List<string> Relations { get; set; }

            public Node(List<double[]> a, List<double> b, List<string> relations)
            {
                // Clone lists to prevent shared reference mutations
                A = a.Select(row => (double[])row.Clone()).ToList();
                B = b.ToList();
                Relations = relations.ToList();
            }
        }

        public static SimplexResult solve(double[] c, double[,] A, double[] b, string[] relations, string[] signRestrictions, bool isMax, TextWriter output)
        {
            int n = c.Length;

            // Setup the initial root subproblem (the continuous LP relaxation)
            var initialA = new List<double[]>();
            for (int i = 0; i < A.GetLength(0); i++)
            {
                var row = new double[n];
                for (int j = 0; j < n; j++)
                {
                    row[j] = A[i, j];
                }
                initialA.Add(row);
            }

            var initialB = b.ToList();
            var initialRelations = relations.ToList();

            var root = new Node(initialA, initialB, initialRelations);

            // Use a stack for Depth-First Search (DFS)
            var stack = new Stack<Node>();
            stack.Push(root);

            double[] bestSolution = null;
            double bestObjective = isMax ? double.NegativeInfinity : double.PositiveInfinity;
            SimplexResult bestResult = null; 
            int nodeCount = 0;

            output.WriteLine("-----> Starting Branch and Bound Simplex Solver ------->");

            while (stack.Count > 0)
            {
                nodeCount++;
                var current = stack.Pop();

                output.WriteLine($"\n-----> Processing Node {nodeCount} Active Stack:{stack.Count} ---");

                double[,] currentA = RowsToMatrix(current.A, n);

                SimplexResult result;

                try
                {
                    // Solve the relaxation for this node
                    result = PrimalSimplex.Solve(c, currentA, current.B.ToArray(), current.Relations.ToArray(), isMax);   
                }
                   catch (InvalidOperationException ex) when (ex.Message.Contains("infeasible"))
                {
                    output.WriteLine("Node is infeasible. Pruning Branch.");
                    continue;
                }
                catch (UnboundedModelException)
                {
                    output.WriteLine("Node is unbounded. The model is unbounded.");
                    throw;
                }
                

                double objective = result.GetOriginalOptimalValue();
                double[] solution = Extraction(result, n);

                output.WriteLine($"Node solved. Objective z = {Math.Round(objective, 3)}\n");

                // Bounding / Pruning
                if (isMax && objective <= bestObjective + eps)
                {
                    output.WriteLine($"Objective {Math.Round(objective, 3)} <= current best Z ({Math.Round(bestObjective, 3)}). Pruning.");
                    continue;
                }

                if (!isMax && objective >= bestObjective - eps)
                {
                    output.WriteLine($"Objective {Math.Round(objective, 3)} >= current best Z ({Math.Round(bestObjective, 3)}). Pruning.");
                    continue;
                }

                // Check integer feasibility
                int fracVarIndex = FindFractionalVariable(solution, signRestrictions);

                if (fracVarIndex == -1)
                {
                    // Solution is integer-feasible and better than current best.
                    bestSolution = solution;
                    bestObjective = objective;
                    bestResult = result;

                    output.WriteLine($"[New Best Integer Solution Found] Z = {Math.Round(bestObjective, 3)}");
                }
                else
                {
                    // Branching
                    double fracValue = solution[fracVarIndex];
                    output.WriteLine($"Branching on fractional variable x{fracVarIndex + 1} = {Math.Round(fracValue, 3)}");

                    // Branch 1: x_i <= floor(value)
                    var leftNode = new Node(current.A, current.B, current.Relations);
                    var leftRow = new double[n];
                    leftRow[fracVarIndex] = 1.0;
                    leftNode.A.Add(leftRow);
                    leftNode.B.Add(Math.Floor(fracValue));
                    leftNode.Relations.Add("<=");
                    stack.Push(leftNode);

                    // Branch 2: x_i >= ceil(value)
                    var rightNode = new Node(current.A, current.B, current.Relations);
                    var rightRow = new double[n];
                    rightRow[fracVarIndex] = 1.0;
                    rightNode.A.Add(rightRow);
                    rightNode.B.Add(Math.Ceiling(fracValue));
                    rightNode.Relations.Add(">=");
                    stack.Push(rightNode);
                }
            }

            output.WriteLine("\n-------> Branch and Bound Completed <-------");
            if (bestSolution == null)
            {
                output.WriteLine("No integer-feasible solution found.");
            }
            else
            {
                output.WriteLine($"Optimal Integer Objective Z = {Math.Round(bestObjective, 3)}");
            }

            return bestResult;
        }

        private static double[,] RowsToMatrix(List<double[]> rows, int n)
        {
            var matrix = new double[rows.Count, n];
            for (int i = 0; i < rows.Count; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    matrix[i, j] = rows[i][j];
                }
            }

            return matrix;
        }

        private static double[] Extraction(SimplexResult result, int n)
        {
            var solution = new double[n];
            int rhscolumn = result.Tableau.GetLength(1) - 1;

            for (int i = 0; i < result.Basis.Length; i++)
            {
                int col = result.Basis[i];
                if (col < n)
                {
                    solution[col] = result.Tableau[i + 1, rhscolumn];
                }
            }
            return solution;
        }

        private static int FindFractionalVariable(double[] solution, string[] signRestrictions)
        {
            for (int j = 0; j < solution.Length; j++)
            {
                bool mustBeInteger = signRestrictions != null && signRestrictions.Length > j &&
                                     (signRestrictions[j] == "int" || signRestrictions[j] == "bin");
                
                if (!mustBeInteger) continue;

                double frac = solution[j] - Math.Floor(solution[j]);
                if (frac > eps && frac < 1.0 - eps)
                {
                    return j;
                }
            }

            return -1;
        }
    }
}
