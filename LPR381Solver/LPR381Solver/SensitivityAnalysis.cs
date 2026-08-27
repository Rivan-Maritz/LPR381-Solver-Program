using System;
using System.Collections.Generic;
using System.Linq;

namespace LPR381Solver
{
    /// <summary>
    /// Sensitivity analysis and duality support built directly on top of the
    /// real solving pipeline: LinearProgrammingModel -> CanonicalFormConverter
    /// -> PrimalSimplexAdapter -> SimplexResult. Every "what changes if..."
    /// question is answered by cloning the original model, applying the
    /// change, and re-running it through the exact same adapter the main
    /// solve path uses.
    /// </summary>
    public class AnalyzeSensitivity
    {
        private const double TOLERANCE = 1e-7;
        private const double MAX_SEARCH_VALUE = 1000000000.0;

        private readonly PrimalSimplexAdapter primalSimplexAdapter = new PrimalSimplexAdapter();

        public LinearProgrammingModel OriginalModel { get; set; }
        public SensitivityResult SolvedResult { get; set; }

        /// <summary>
        /// Solves OriginalModel through the primal simplex adapter and stores
        /// the result as the baseline every other method compares against.
        /// </summary>
        public SensitivityResult SolveOriginal()
        {
            if (OriginalModel == null)
            {
                throw new InvalidOperationException(
                    "OriginalModel has not been assigned.");
            }

            SolvedResult = TrySolve(OriginalModel);
            return SolvedResult;
        }


        // 1. NON-BASIC VARIABLE OBJECTIVE COEFFICIENT

        public Range GetNonBasicVariableRange(int variableIndex)
        {
            ValidateVariableIndex(variableIndex);
            ValidateSolvedResult();

            if (IsVariableBasic(variableIndex))
            {
                throw new InvalidOperationException(
                    "The selected variable is basic, not non-basic.");
            }

            double currentValue = OriginalModel.ObjectiveCoefficients[variableIndex];

            return FindBasisPreservingRange(
                currentValue,
                (model, value) =>
                {
                    model.ObjectiveCoefficients[variableIndex] = value;
                });
        }

        public SensitivityResult ApplyNonBasicVariableChange(
            int variableIndex,
            double newValue)
        {
            ValidateVariableIndex(variableIndex);
            ValidateSolvedResult();

            if (IsVariableBasic(variableIndex))
            {
                throw new InvalidOperationException(
                    "The selected variable is basic, not non-basic.");
            }

            LinearProgrammingModel changedModel = CloneModel(OriginalModel);

            changedModel.ObjectiveCoefficients[variableIndex] = newValue;

            return TrySolve(changedModel);
        }


        // 2. BASIC VARIABLE OBJECTIVE COEFFICIENT

        public Range GetBasicVariableRange(int variableIndex)
        {
            ValidateVariableIndex(variableIndex);
            ValidateSolvedResult();

            if (!IsVariableBasic(variableIndex))
            {
                throw new InvalidOperationException(
                    "The selected variable is non-basic, not basic.");
            }

            double currentValue =
                OriginalModel.ObjectiveCoefficients[variableIndex];

            return FindBasisPreservingRange(
                currentValue,
                (model, value) =>
                {
                    model.ObjectiveCoefficients[variableIndex] = value;
                });
        }

        public SensitivityResult ApplyBasicVariableChange(
            int variableIndex,
            double newValue)
        {
            ValidateVariableIndex(variableIndex);
            ValidateSolvedResult();

            if (!IsVariableBasic(variableIndex))
            {
                throw new InvalidOperationException(
                    "The selected variable is non-basic, not basic.");
            }

            LinearProgrammingModel changedModel = CloneModel(OriginalModel);

            changedModel.ObjectiveCoefficients[variableIndex] = newValue;

            return TrySolve(changedModel);
        }


        // 3. RIGHT-HAND SIDE RANGING

        public Range GetRHSRange(int constraintIndex)
        {
            ValidateConstraintIndex(constraintIndex);
            ValidateSolvedResult();

            double currentValue =
                OriginalModel.Constraints[constraintIndex].RightHandSide;

            return FindBasisPreservingRange(
                currentValue,
                (model, value) =>
                {
                    LinearConstraint existing = model.Constraints[constraintIndex];

                    model.Constraints[constraintIndex] =
                        new LinearConstraint(
                            existing.Coefficients,
                            existing.Relation,
                            value);
                });
        }

        public SensitivityResult ApplyRHSChange(
            int constraintIndex,
            double newRHS)
        {
            ValidateConstraintIndex(constraintIndex);
            ValidateSolvedResult();

            LinearProgrammingModel changedModel = CloneModel(OriginalModel);

            LinearConstraint existing = changedModel.Constraints[constraintIndex];

            changedModel.Constraints[constraintIndex] =
                new LinearConstraint(
                    existing.Coefficients,
                    existing.Relation,
                    newRHS);

            return TrySolve(changedModel);
        }


        // 4. NON-BASIC VARIABLE COLUMN RANGING

        public Range GetVariableColumnRange(int variableIndex)
        {
            ValidateVariableIndex(variableIndex);
            ValidateSolvedResult();

            if (IsVariableBasic(variableIndex))
            {
                throw new InvalidOperationException(
                    "The selected variable must be non-basic for column sensitivity analysis.");
            }

            double[] originalColumn =
                OriginalModel.Constraints
                    .Select(c => c.Coefficients[variableIndex])
                    .ToArray();

            double currentScale = 1.0;

            return FindBasisPreservingRange(
                currentScale,
                (model, scale) =>
                {
                    for (int i = 0; i < model.Constraints.Length; i++)
                    {
                        model.Constraints[i]
                            .Coefficients[variableIndex]
                            = originalColumn[i] * scale;
                    }
                });
        }


        public SensitivityResult ApplyVariableColumnChange(
            int variableIndex,
            double[] newColumn)
        {
            ValidateVariableIndex(variableIndex);
            ValidateSolvedResult();

            if (newColumn == null)
            {
                throw new ArgumentNullException(nameof(newColumn));
            }

            if (newColumn.Length != OriginalModel.ConstraintCount)
            {
                throw new ArgumentException(
                    "The new column must contain one coefficient for every constraint.");
            }

            if (IsVariableBasic(variableIndex))
            {
                throw new InvalidOperationException(
                    "The selected variable must be non-basic for column sensitivity analysis.");
            }

            LinearProgrammingModel changedModel = CloneModel(OriginalModel);

            for (int i = 0; i < changedModel.Constraints.Length; i++)
            {
                changedModel.Constraints[i]
                    .Coefficients[variableIndex] = newColumn[i];
            }

            return TrySolve(changedModel);
        }


        // 5. ADD A NEW ACTIVITY (DECISION VARIABLE)

        public SensitivityResult AddNewActivity(
            double[] newColumnCoefficients,
            double objectiveCoefficient,
            VariableRestriction restriction = VariableRestriction.NonNegative)
        {
            ValidateSolvedResult();

            if (newColumnCoefficients == null)
            {
                throw new ArgumentNullException(
                    nameof(newColumnCoefficients));
            }

            if (newColumnCoefficients.Length != OriginalModel.ConstraintCount)
            {
                throw new ArgumentException(
                    "The new activity must contain one coefficient for every constraint.");
            }

            double[] newObjective =
                OriginalModel.ObjectiveCoefficients
                    .Concat(new[] { objectiveCoefficient })
                    .ToArray();

            VariableRestriction[] newRestrictions =
                OriginalModel.VariableRestrictions
                    .Concat(new[] { restriction })
                    .ToArray();

            var newConstraints = new List<LinearConstraint>();

            for (int i = 0; i < OriginalModel.ConstraintCount; i++)
            {
                LinearConstraint existing = OriginalModel.Constraints[i];

                double[] coefficients =
                    existing.Coefficients
                        .Concat(new[] { newColumnCoefficients[i] })
                        .ToArray();

                newConstraints.Add(
                    new LinearConstraint(
                        coefficients,
                        existing.Relation,
                        existing.RightHandSide));
            }

            LinearProgrammingModel changedModel =
                new LinearProgrammingModel(
                    OriginalModel.ObjectiveSense,
                    newObjective,
                    newConstraints,
                    newRestrictions);

            return TrySolve(changedModel);
        }


        // 6. ADD A NEW CONSTRAINT

        public SensitivityResult AddNewConstraint(
            double[] coefficients,
            ConstraintRelation relation,
            double rhs)
        {
            ValidateSolvedResult();

            if (coefficients == null)
            {
                throw new ArgumentNullException(nameof(coefficients));
            }

            if (coefficients.Length != OriginalModel.VariableCount)
            {
                throw new ArgumentException(
                    "The new constraint must contain one coefficient for every decision variable.");
            }

            List<LinearConstraint> newConstraints =
                OriginalModel.Constraints.ToList();

            newConstraints.Add(
                new LinearConstraint(coefficients, relation, rhs));

            LinearProgrammingModel changedModel =
                new LinearProgrammingModel(
                    OriginalModel.ObjectiveSense,
                    OriginalModel.ObjectiveCoefficients,
                    newConstraints,
                    OriginalModel.VariableRestrictions);

            return TrySolve(changedModel);
        }


        // 7. SHADOW PRICES / B-INVERSE
        //
        // These read straight off the final tableau that the primal simplex
        // already produced for OriginalModel, rather than re-deriving them
        // by perturbing and re-solving.

        public double[] GetShadowPrices()
        {
            ValidateSolvedResult();

            return SolvedResult.Simplex.GetShadowPrices();
        }

        public double[,] GetBInverse()
        {
            ValidateSolvedResult();

            return SolvedResult.Simplex.GetBInverse();
        }


        // 8. DUALITY

        public LinearProgrammingModel ApplyDuality()
        {
            if (OriginalModel == null)
            {
                throw new InvalidOperationException(
                    "OriginalModel has not been assigned.");
            }

            // Max primal -> Min dual
            // Min primal -> Max dual
            ObjectiveSense dualSense =
                OriginalModel.ObjectiveSense == ObjectiveSense.Maximize
                    ? ObjectiveSense.Minimize
                    : ObjectiveSense.Maximize;

            // --------------------------------------------------------
            // Dual objective coefficients
            //
            // The primal RHS values become the dual objective
            // coefficients.
            // --------------------------------------------------------

            double[] dualObjective =
                OriginalModel.Constraints
                    .Select(c => c.RightHandSide)
                    .ToArray();


            var dualRestrictions = new List<VariableRestriction>();

            foreach (LinearConstraint constraint in OriginalModel.Constraints)
            {
                if (constraint.Relation == ConstraintRelation.Equal)
                {
                    // Equality constraint -> unrestricted dual variable
                    dualRestrictions.Add(VariableRestriction.Unrestricted);
                }
                else if (OriginalModel.ObjectiveSense == ObjectiveSense.Maximize)
                {
                    // Max primal:
                    // <= -> y >= 0
                    // >= -> y <= 0
                    dualRestrictions.Add(
                        constraint.Relation == ConstraintRelation.LessThanOrEqual
                            ? VariableRestriction.NonNegative
                            : VariableRestriction.NonPositive);
                }
                else
                {
                    // Min primal:
                    // <= -> y <= 0
                    // >= -> y >= 0
                    dualRestrictions.Add(
                        constraint.Relation == ConstraintRelation.LessThanOrEqual
                            ? VariableRestriction.NonPositive
                            : VariableRestriction.NonNegative);
                }
            }


            var dualConstraints = new List<LinearConstraint>();

            for (int j = 0; j < OriginalModel.VariableCount; j++)
            {
                double[] coefficients =
                    OriginalModel.Constraints
                        .Select(c => c.Coefficients[j])
                        .ToArray();

                ConstraintRelation relation;

                VariableRestriction primalRestriction =
                    OriginalModel.VariableRestrictions[j];

                if (primalRestriction == VariableRestriction.Unrestricted)
                {
                    relation = ConstraintRelation.Equal;
                }
                else if (OriginalModel.ObjectiveSense == ObjectiveSense.Maximize)
                {
                    // Max primal -> Min dual
                    //
                    // x <= 0 -> <=
                    // x >= 0 -> >=
                    relation =
                        primalRestriction == VariableRestriction.NonPositive
                            ? ConstraintRelation.LessThanOrEqual
                            : ConstraintRelation.GreaterThanOrEqual;
                }
                else
                {
                    // Min primal -> Max dual
                    //
                    // x <= 0 -> >=
                    // x >= 0 -> <=
                    relation =
                        primalRestriction == VariableRestriction.NonPositive
                            ? ConstraintRelation.GreaterThanOrEqual
                            : ConstraintRelation.LessThanOrEqual;
                }

                dualConstraints.Add(
                    new LinearConstraint(
                        coefficients,
                        relation,
                        OriginalModel.ObjectiveCoefficients[j]));
            }

            return new LinearProgrammingModel(
                dualSense,
                dualObjective,
                dualConstraints,
                dualRestrictions);
        }


        public SensitivityResult SolveDualModel()
        {
            LinearProgrammingModel dual = ApplyDuality();

            return TrySolve(dual);
        }


        public string CheckDualityStrength()
        {
            ValidateSolvedResult();

            SensitivityResult primalResult = TrySolve(CloneModel(OriginalModel));

            if (!primalResult.IsOptimal)
            {
                return "weak";
            }

            LinearProgrammingModel dual = ApplyDuality();

            SensitivityResult dualResult = TrySolve(dual);

            if (!dualResult.IsOptimal)
            {
                return "weak";
            }

            double difference =
                Math.Abs(
                    primalResult.ObjectiveValue -
                    dualResult.ObjectiveValue);

            if (difference <= 0.0001)
            {
                return "strong";
            }

            return "weak";
        }



        // --------------------------------------------------------------
        // Solving: this is the seam that connects sensitivity analysis
        // to the real primal simplex pipeline. Every operation above
        // funnels through here so it always uses the same conversion
        // and solving logic as a normal "Solve" from the main form.
        // --------------------------------------------------------------

        private SensitivityResult TrySolve(LinearProgrammingModel model)
        {
            try
            {
                CanonicalForm canonical = CanonicalFormConverter.Convert(model);

                SolverExecution execution =
                    primalSimplexAdapter.Solve(model, canonical);

                return SensitivityResult.Optimal(
                    model,
                    canonical,
                    execution.SimplexResult);
            }
            catch (AlgorithmCompatibilityException ex)
            {
                return SensitivityResult.Failed(ex.Message);
            }
            catch (UnboundedModelException ex)
            {
                return SensitivityResult.Failed(ex.Message);
            }
            catch (InfeasibleModelException ex)
            {
                return SensitivityResult.Failed(ex.Message);
            }
            catch (ModelValidationException ex)
            {
                return SensitivityResult.Failed(ex.Message);
            }
        }


        private Range FindBasisPreservingRange(
            double currentValue,
            Action<LinearProgrammingModel, double> modifier)
        {
            ValidateSolvedResult();

            double? lower =
                FindBoundary(
                    currentValue,
                    -1,
                    modifier);

            double? upper =
                FindBoundary(
                    currentValue,
                    +1,
                    modifier);

            return new Range
            {
                LowerBound =
                    lower.HasValue
                        ? lower.Value
                        : double.NegativeInfinity,

                UpperBound =
                    upper.HasValue
                        ? upper.Value
                        : double.PositiveInfinity
            };
        }


        /*
         * Searches in one direction until the current optimal basis
         * stops being optimal.
         */
        private double? FindBoundary(
            double currentValue,
            int direction,
            Action<LinearProgrammingModel, double> modifier)
        {
            double stableValue = currentValue;

            double step =
                Math.Max(
                    1.0,
                    Math.Abs(currentValue) * 0.1);

            for (int iteration = 0;
                 iteration < 60;
                 iteration++)
            {
                double candidate =
                    currentValue +
                    (direction * step);

                if (Math.Abs(candidate) >
                    MAX_SEARCH_VALUE)
                {
                    return null;
                }

                SensitivityResult result =
                    SolveWithParameter(
                        candidate,
                        modifier);

                if (HasSameBasis(result))
                {
                    stableValue = candidate;
                    step *= 2.0;
                }
                else
                {
                    return BinarySearchBoundary(
                        stableValue,
                        candidate,
                        modifier);
                }
            }

            return null;
        }


        private double BinarySearchBoundary(
            double stableValue,
            double unstableValue,
            Action<LinearProgrammingModel, double> modifier)
        {
            double stable = stableValue;
            double unstable = unstableValue;

            for (int iteration = 0;
                 iteration < 70;
                 iteration++)
            {
                double middle =
                    (stable + unstable) / 2.0;

                if (Math.Abs(stable - unstable) <
                    TOLERANCE *
                    Math.Max(1.0, Math.Abs(middle)))
                {
                    break;
                }

                SensitivityResult result =
                    SolveWithParameter(
                        middle,
                        modifier);

                if (HasSameBasis(result))
                {
                    stable = middle;
                }
                else
                {
                    unstable = middle;
                }
            }

            return stable;
        }


        private SensitivityResult SolveWithParameter(
            double value,
            Action<LinearProgrammingModel, double> modifier)
        {
            try
            {
                LinearProgrammingModel model =
                    CloneModel(OriginalModel);

                modifier(model, value);

                return TrySolve(model);
            }
            catch
            {
                return null;
            }
        }


        private bool HasSameBasis(
            SensitivityResult result)
        {
            if (result == null ||
                !result.IsOptimal ||
                result.Simplex == null ||
                SolvedResult == null ||
                SolvedResult.Simplex == null)
            {
                return false;
            }

            int[] originalBasis = SolvedResult.Simplex.Basis;
            int[] newBasis = result.Simplex.Basis;

            if (originalBasis.Length != newBasis.Length)
            {
                return false;
            }

            for (int i = 0;
                 i < originalBasis.Length;
                 i++)
            {
                if (originalBasis[i] != newBasis[i])
                {
                    return false;
                }
            }

            return true;
        }


        private bool IsVariableBasic(int variableIndex)
        {
            if (SolvedResult == null ||
                SolvedResult.Simplex == null ||
                SolvedResult.Canonical == null)
            {
                return false;
            }

            VariableRestriction restriction =
                OriginalModel.VariableRestrictions[variableIndex];

            if (restriction == VariableRestriction.Unrestricted)
            {
                throw new InvalidOperationException(
                    "Unrestricted (urs) variables do not have a single basic/non-basic column.");
            }

            int canonicalColumn =
                SolvedResult.Canonical
                    .OriginalVariableMappings[variableIndex]
                    .CanonicalIndexes[0];

            return SolvedResult.Simplex.Basis.Contains(canonicalColumn);
        }


        private void ValidateSolvedResult()
        {
            if (OriginalModel == null)
            {
                throw new InvalidOperationException(
                    "OriginalModel has not been assigned.");
            }

            if (SolvedResult == null)
            {
                throw new InvalidOperationException(
                    "The original model has not been solved yet. Call SolveOriginal() first.");
            }

            if (!SolvedResult.IsOptimal)
            {
                throw new InvalidOperationException(
                    "Sensitivity analysis requires an optimal solution. " +
                    SolvedResult.FailureReason);
            }

            if (SolvedResult.Simplex == null || SolvedResult.Canonical == null)
            {
                throw new InvalidOperationException(
                    "The solved result does not contain a final tableau.");
            }
        }


        private void ValidateVariableIndex(
            int variableIndex)
        {
            if (OriginalModel == null)
            {
                throw new InvalidOperationException(
                    "OriginalModel has not been assigned.");
            }

            if (variableIndex < 0 ||
                variableIndex >=
                OriginalModel.VariableCount)
            {
                throw new ArgumentOutOfRangeException(nameof(variableIndex));
            }
        }


        private void ValidateConstraintIndex(
            int constraintIndex)
        {
            if (OriginalModel == null)
            {
                throw new InvalidOperationException(
                    "OriginalModel has not been assigned.");
            }

            if (constraintIndex < 0 ||
                constraintIndex >=
                OriginalModel.ConstraintCount)
            {
                throw new ArgumentOutOfRangeException(nameof(constraintIndex));
            }
        }



        private static LinearProgrammingModel CloneModel(LinearProgrammingModel original)
        {
            IEnumerable<LinearConstraint> constraints =
                original.Constraints.Select(c =>
                    new LinearConstraint(
                        (double[])c.Coefficients.Clone(),
                        c.Relation,
                        c.RightHandSide));

            return new LinearProgrammingModel(
                original.ObjectiveSense,
                (double[])original.ObjectiveCoefficients.Clone(),
                constraints,
                (VariableRestriction[])original.VariableRestrictions.Clone());
        }
    }


    /// <summary>
    /// Outcome of running a (possibly modified) model through the primal
    /// simplex adapter for sensitivity analysis purposes. Mirrors what
    /// MainForm gets back from a normal solve (SimplexResult), plus the
    /// LinearProgrammingModel/CanonicalForm that produced it and a reason
    /// when the model could not be solved to optimality.
    /// </summary>
    public sealed class SensitivityResult
    {
        private SensitivityResult()
        {
        }

        public bool IsOptimal { get; private set; }
        public LinearProgrammingModel Model { get; private set; }
        public CanonicalForm Canonical { get; private set; }
        public SimplexResult Simplex { get; private set; }
        public string FailureReason { get; private set; }

        public double ObjectiveValue =>
            IsOptimal && Simplex != null
                ? Simplex.GetOriginalOptimalValue()
                : double.NaN;

        internal static SensitivityResult Optimal(
            LinearProgrammingModel model,
            CanonicalForm canonical,
            SimplexResult simplex)
        {
            return new SensitivityResult
            {
                IsOptimal = true,
                Model = model,
                Canonical = canonical,
                Simplex = simplex
            };
        }

        internal static SensitivityResult Failed(string reason)
        {
            return new SensitivityResult
            {
                IsOptimal = false,
                FailureReason = reason
            };
        }
    }


    public class Range
    {
        public double LowerBound { get; set; }

        public double UpperBound { get; set; }
    }
}