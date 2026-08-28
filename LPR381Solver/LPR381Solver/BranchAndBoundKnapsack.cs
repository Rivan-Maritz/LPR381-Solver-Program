using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LPR381Solver
{
    // Branch & Bound for the 0/1 knapsack problem: maximise sum(profit[j] * xj)
    // subject to one weight constraint sum(weight[j] * xj) <= capacity, with
    // every xj restricted to 0 or 1.
    //
    // This uses the classic knapsack-specific bounding rule instead of solving
    // a full LP relaxation with the simplex method at every node:
    //   1. Sort items by profit-to-weight ratio, most efficient first.
    //   2. At each node, some items are already fixed IN, some are fixed OUT,
    //      and the rest are still undecided. The bound for that node is: take
    //      all the fixed-in items, then greedily add undecided items in ratio
    //      order until the capacity would be exceeded, and add a *fraction* of
    //      the item that doesn't fully fit. That fractional-fill total is an
    //      upper bound on anything achievable below this node - it's exactly
    //      the answer to the LP relaxation of the remaining sub-problem.
    //   3. If a node's bound can't beat the best whole-number (0/1) solution
    //      found so far, there is no point exploring it further.
    //   4. Otherwise branch on the next undecided item in ratio order: one
    //      child fixes it IN, the other fixes it OUT.
    //
    // At the end, the winning 0/1 assignment is re-solved as a plain LP (each
    // variable pinned to its chosen 0 or 1 value via an equality row) through
    // PrimalSimplex.Solve, so callers get back a normal SimplexResult/tableau -
    // same as every other algorithm in this project. The rest of the UI
    // (iteration grid, B-inverse, shadow prices, sensitivity analysis) doesn't
    // need to know this was actually solved by ratio-bounded branch & bound
    // rather than the simplex method.
    public static class BranchAndBoundKnapsack
    {
        private const double Tolerance = 1e-9;
        private const int MaxNodesExplored = 200000;

        private enum Decision
        {
            Undecided,
            In,
            Out
        }

        public static SimplexResult Solve(
            double[] profit,
            double[] weight,
            double capacity,
            bool isMax,
            TextWriter output)
        {
            if (!isMax)
            {
                throw new AlgorithmCompatibilityException(
                    "Branch & Bound Knapsack only supports maximisation models.");
            }

            int n = profit.Length;

            // Most "bang per unit weight" first - this is what the bounding
            // rule (ComputeBound) walks in order, and also the order new
            // branch decisions get made in.
            int[] order = Enumerable.Range(0, n)
                .OrderByDescending(j => weight[j] <= Tolerance ? double.PositiveInfinity : profit[j] / weight[j])
                .ToArray();

            var bestDecisions = new Decision[n];
            for (int j = 0; j < n; j++)
                bestDecisions[j] = Decision.Out;

            double bestProfit = 0.0;
            bool foundAny = false;

            var rootDecisions = new Decision[n];
            for (int j = 0; j < n; j++)
                rootDecisions[j] = Decision.Undecided;

            var pending = new Stack<Decision[]>();
            pending.Push(rootDecisions);

            int nodesExplored = 0;

            while (pending.Count > 0)
            {
                if (nodesExplored >= MaxNodesExplored)
                {
                    output.WriteLine("Reached the maximum number of branch-and-bound nodes explored - stopping with the best solution found so far.");
                    break;
                }

                Decision[] decisions = pending.Pop();
                nodesExplored++;

                double usedWeight = 0.0;
                double usedProfit = 0.0;
                for (int j = 0; j < n; j++)
                {
                    if (decisions[j] != Decision.In)
                        continue;
                    usedWeight += weight[j];
                    usedProfit += profit[j];
                }

                if (usedWeight > capacity + Tolerance)
                {
                    // Fixing every "In" item already overflows the capacity -
                    // nothing under this node can ever be feasible.
                    continue;
                }

                double bound = ComputeBound(decisions, order, weight, profit, capacity, usedWeight, usedProfit);

                if (foundAny && bound <= bestProfit + Tolerance)
                    continue; // can't beat the incumbent - prune this whole branch

                int nextIndex = Array.IndexOf(decisions, Decision.Undecided);

                if (nextIndex == -1)
                {
                    // Every item has been decided and it's still feasible -
                    // this is a complete, valid 0/1 solution.
                    if (!foundAny || usedProfit > bestProfit + Tolerance)
                    {
                        foundAny = true;
                        bestProfit = usedProfit;
                        bestDecisions = (Decision[])decisions.Clone();
                        output.WriteLine($"Node {nodesExplored}: complete solution, profit = {Math.Round(usedProfit, 3)} (new incumbent).");
                    }
                    continue;
                }

                int branchItem = -1;
                foreach (int candidate in order)
                {
                    if (decisions[candidate] == Decision.Undecided)
                    {
                        branchItem = candidate;
                        break;
                    }
                }

                if (branchItem == -1)
                    continue; // shouldn't happen, but guards against an infinite loop

                var withItem = (Decision[])decisions.Clone();
                withItem[branchItem] = Decision.In;
                var withoutItem = (Decision[])decisions.Clone();
                withoutItem[branchItem] = Decision.Out;

                // Explore "include" first - it tends to find good incumbents
                // sooner, which makes later pruning more effective.
                pending.Push(withoutItem);
                pending.Push(withItem);
            }

            if (!foundAny)
            {
                throw new InfeasibleModelException(
                    "Branch & Bound Knapsack could not find a feasible 0/1 solution within the given capacity.");
            }

            output.WriteLine();
            output.WriteLine("=== Best 0/1 solution found ===");
            for (int j = 0; j < n; j++)
                output.WriteLine($"x{j + 1} = {(bestDecisions[j] == Decision.In ? 1 : 0)}");
            output.WriteLine($"Z = {Math.Round(bestProfit, 3)}");

            return SolveFixedAssignment(profit, weight, capacity, bestDecisions);
        }

        // The knapsack-specific "fractional fill" bound: start from what's
        // already fixed in, then greedily add undecided items in profit/weight
        // ratio order, taking a fractional slice of the first item that
        // doesn't fully fit. This equals the LP relaxation's optimal value for
        // everything still undecided, which is always >= anything achievable
        // with whole items only - so it's a valid upper bound for pruning.
        private static double ComputeBound(
            Decision[] decisions,
            int[] order,
            double[] weight,
            double[] profit,
            double capacity,
            double usedWeight,
            double usedProfit)
        {
            double remainingCapacity = capacity - usedWeight;
            double bound = usedProfit;

            foreach (int j in order)
            {
                if (decisions[j] != Decision.Undecided)
                    continue;

                if (weight[j] <= remainingCapacity + Tolerance)
                {
                    remainingCapacity -= weight[j];
                    bound += profit[j];
                }
                else
                {
                    if (weight[j] > Tolerance)
                        bound += profit[j] * (remainingCapacity / weight[j]);
                    break;
                }
            }

            return bound;
        }

        // Re-solves the winning 0/1 assignment as a plain LP (each variable's
        // value pinned via an equality row) so the caller gets back a normal
        // SimplexResult/tableau, exactly like every other algorithm here.
        private static SimplexResult SolveFixedAssignment(
            double[] profit,
            double[] weight,
            double capacity,
            Decision[] decisions)
        {
            int n = profit.Length;
            var a = new double[n + 1, n];
            var b = new double[n + 1];
            var relations = new string[n + 1];

            for (int j = 0; j < n; j++)
                a[0, j] = weight[j];
            b[0] = capacity;
            relations[0] = "<=";

            for (int j = 0; j < n; j++)
            {
                a[j + 1, j] = 1.0;
                b[j + 1] = decisions[j] == Decision.In ? 1.0 : 0.0;
                relations[j + 1] = "=";
            }

            return PrimalSimplex.Solve(profit, a, b, relations, isMax: true);
        }
    }
}