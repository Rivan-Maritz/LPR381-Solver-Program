using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LPR381Solver
{
    // This solves an Integer Programming model using the Cutting Plane algorithm.
    // The idea in plain terms:
    //   1. Solve the model as a normal LP first, ignoring the "must be a whole
    //      number" requirement. This is called the "relaxed" LP.
    //   2. Look at the answer. If every variable that needs to be an integer
    //      already came out as a whole number, we're done.
    //   3. If not, pick one variable that came out fractional (e.g. x2 = 3.4) and
    //      add a new constraint (a "cut") that removes that fractional answer
    //      without removing any answer that was already a valid whole number.
    //   4. Solve again with the new constraint added, and repeat from step 2.
    // We reuse PrimalSimplex.Solve() to actually solve each relaxed LP - this
    // class is really just a loop around that, plus the logic for picking and
    // adding cuts.
    //
    // Note: this uses a simple "round down" cut (xj <= floor(current value))
    // rather than a full Gomory fractional cut. A proper Gomory cut is more
    // textbook-correct, but it mixes small fractional numbers with the Big-M
    // penalty already used in PrimalSimplex.cs, which can cause floating point
    // rounding errors and false "infeasible" results. This simpler cut is more
    // reliable, at the cost of sometimes stopping at a valid integer answer that
    // isn't the absolute best one, rather than the guaranteed optimal answer.
    public static class CuttingPlane
    {
        private const double Eps = 1e-6;
        private const int MaxCuts = 25; // a safety limit so we can't loop forever if something goes wrong

        // Old version, kept so anything already calling this still compiles - it
        // just calls the new version below and throws away the extra SimplexResult.
        public static double[] Solve(double[] c, double[,] A, double[] b, string[] relations,
            string[] signRestrictions, bool isMax, TextWriter output)
        {
            return Solve(c, A, b, relations, signRestrictions, isMax, output, out _);
        }

        // Same as above, but also hands back the SimplexResult from the relaxed LP
        // that finally gave the all-integer answer. The UI needs this to show the
        // tableau, B-inverse and shadow prices the same way it does for Primal Simplex.
        //
        // signRestrictions holds one entry per decision variable, e.g. "int",
        // "bin", "+", "-" or "urs" - in the same order as the objective function.
        // Only variables marked "int" or "bin" are forced to come out as whole
        // numbers here; the rest are allowed to be fractional.
        public static double[] Solve(double[] c, double[,] A, double[] b, string[] relations,
            string[] signRestrictions, bool isMax, TextWriter output, out SimplexResult finalResult)
        {
            // We copy the constraints into lists instead of fixed-size arrays,
            // because every cut we add means one more row - lists make that easy.
            int n = c.Length;
            var rows = new List<double[]>();
            for (int i = 0; i < A.GetLength(0); i++)
            {
                var row = new double[n];
                for (int j = 0; j < n; j++) row[j] = A[i, j];
                rows.Add(row);
            }
            var rhs = b.ToList();
            var rels = relations.ToList();

            finalResult = null;
            int cutCount = 0;
            while (true)
            {
                output.WriteLine($"=== Cutting Plane: solving relaxed LP (cut {cutCount}) ===");

                // build the constraint matrix fresh each time, since a cut might
                // have been added to "rows" on the last loop
                double[,] currentA = RowsToMatrix(rows, n);

                SimplexResult result;
                try
                {
                    result = PrimalSimplex.Solve(c, currentA, rhs.ToArray(), rels.ToArray(), isMax);
                }
                catch (Exception ex)
                {
                    output.WriteLine($"Could not solve the relaxed LP: {ex.Message}");
                    return null;
                }

                double[] solution = ExtractSolution(result, n);
                output.WriteLine(PrimalSimplex.TableauToString(result.Tableau));

                int fracVarIndex = FindFractionalVariable(solution, signRestrictions);
                if (fracVarIndex == -1)
                {
                    output.WriteLine("=== All integer-restricted variables are whole numbers - done ===");
                    for (int j = 0; j < n; j++)
                        output.WriteLine($"x{j + 1} = {Math.Round(solution[j], 3)}");
                    output.WriteLine($"Z = {Math.Round(result.GetOptimalValue(), 3)}");
                    finalResult = result;
                    return solution;
                }

                if (cutCount >= MaxCuts)
                {
                    output.WriteLine("Reached the maximum number of cuts without an integer answer - stopping.");
                    finalResult = result;
                    return solution;
                }

                // Add a simple cut: xj <= floor(current value). This chops off the
                // exact fractional point we just found, but keeps every valid
                // whole-number point, because no valid integer answer could ever
                // have xj bigger than its own floor anyway.
                var cutRow = new double[n];
                cutRow[fracVarIndex] = 1;
                rows.Add(cutRow);
                rhs.Add(Math.Floor(solution[fracVarIndex]));
                rels.Add("<=");

                cutCount++;
                output.WriteLine($"Added cut #{cutCount} on x{fracVarIndex + 1} (value was {Math.Round(solution[fracVarIndex], 3)})");
                output.WriteLine();
            }
        }

        // Turns our list-of-rows back into a normal 2D array, since that's what
        // PrimalSimplex.Solve() expects.
        private static double[,] RowsToMatrix(List<double[]> rows, int n)
        {
            var matrix = new double[rows.Count, n];
            for (int i = 0; i < rows.Count; i++)
                for (int j = 0; j < n; j++)
                    matrix[i, j] = rows[i][j];
            return matrix;
        }

        // Reads the x-values back out of the final tableau, using the basis to
        // know which row holds the value for each variable.
        private static double[] ExtractSolution(SimplexResult result, int n)
        {
            var solution = new double[n];
            int rhsCol = result.Tableau.GetLength(1) - 1;
            for (int i = 0; i < result.Basis.Length; i++)
            {
                int col = result.Basis[i];
                if (col < n)
                    solution[col] = result.Tableau[i + 1, rhsCol];
            }
            return solution;
        }

        // Looks through the solution for the first variable that (a) is supposed
        // to be a whole number and (b) currently isn't one.
        private static int FindFractionalVariable(double[] solution, string[] signRestrictions)
        {
            for (int j = 0; j < solution.Length; j++)
            {
                bool mustBeInteger = signRestrictions != null && signRestrictions.Length > j &&
                    (signRestrictions[j] == "int" || signRestrictions[j] == "bin");
                if (!mustBeInteger) continue;

                double frac = solution[j] - Math.Floor(solution[j]);
                if (frac > Eps && frac < 1 - Eps)
                    return j;
            }
            return -1;
        }
    }
}