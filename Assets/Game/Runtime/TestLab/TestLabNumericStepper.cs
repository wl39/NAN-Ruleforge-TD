using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UnityView.TestLab
{
    /// <summary>
    /// 모바일 WebGL에서도 시스템 키보드 없이 TestLab 숫자를 조절한다.
    /// 숫자 해석과 표시만 담당하며 실제 TestLab 명령은 소유하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TestLabNumericStepper : MonoBehaviour
    {
        private InputField input;
        private Button decrementButton;
        private Button incrementButton;
        private double fallbackValue;
        private double step;
        private double minimum;
        private double maximum;
        private bool wholeNumber;
        private Action onStepped;

        public InputField Input => input;
        public Button DecrementButton => decrementButton;
        public Button IncrementButton => incrementButton;
        public double StepSize => step;

        public void Configure(
            InputField targetInput,
            Button decrement,
            Button increment,
            double fallback,
            double stepSize,
            double minimumValue,
            double maximumValue,
            bool useWholeNumbers,
            Action steppedAction = null)
        {
            input = targetInput;
            decrementButton = decrement;
            incrementButton = increment;
            fallbackValue = fallback;
            step = Math.Max(0.000001d, Math.Abs(stepSize));
            minimum = Math.Min(minimumValue, maximumValue);
            maximum = Math.Max(minimumValue, maximumValue);
            wholeNumber = useWholeNumbers;
            onStepped = steppedAction;

            if (decrementButton != null)
            {
                decrementButton.onClick.RemoveListener(Decrement);
                decrementButton.onClick.AddListener(Decrement);
            }

            if (incrementButton != null)
            {
                incrementButton.onClick.RemoveListener(Increment);
                incrementButton.onClick.AddListener(Increment);
            }
        }

        public void Decrement()
        {
            ApplyStep(-step);
        }

        public void Increment()
        {
            ApplyStep(step);
        }

        private void ApplyStep(double delta)
        {
            if (input == null)
            {
                return;
            }

            double value = ReadCurrentValue();
            value = Math.Max(
                minimum,
                Math.Min(maximum, value + delta));
            if (wholeNumber)
            {
                value = Math.Round(
                    value,
                    MidpointRounding.AwayFromZero);
            }

            string formatted = wholeNumber
                ? Convert.ToInt64(value)
                    .ToString(CultureInfo.InvariantCulture)
                : value.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);
            input.SetTextWithoutNotify(formatted);
            input.ForceLabelUpdate();
            onStepped?.Invoke();
        }

        private double ReadCurrentValue()
        {
            if (input != null &&
                (double.TryParse(
                     input.text,
                     NumberStyles.Float,
                     CultureInfo.InvariantCulture,
                     out double value) ||
                 double.TryParse(
                     input.text,
                     NumberStyles.Float,
                     CultureInfo.CurrentCulture,
                     out value)))
            {
                return Math.Max(
                    minimum,
                    Math.Min(maximum, value));
            }

            return Math.Max(
                minimum,
                Math.Min(maximum, fallbackValue));
        }
    }
}
