using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using log4net;
using Tensorflow;
using static Tensorflow.KerasApi;
using NumSharp;
using Tensorflow.Keras;
using Tensorflow.Keras.ArgsDefinition;
using Tensorflow.Keras.Layers;
using Tensorflow.Keras.Losses;
using Tensorflow.Keras.Optimizers;

namespace TraderTools.ML
{
    public class Trainer
    {
        private class TensorFlowTextWriter : TextWriter
        {
            private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

            public override void Write(string value)
            {
                Log.Info(value);
            }

            public override void WriteLine(string value)
            {
                Log.Info(value);
            }

            public override Encoding Encoding { get; }
        }

        public Trainer()
        {
            Binding.tf_output_redirect = new TensorFlowTextWriter();
        }

        public void Test()
        {
            var xValues = new List<float>();
            var yValues = new List<float>();
            for (var i = 0; i < 10; i++)
            {
                for (var ii = 0; ii < 100; ii++)
                {
                    xValues.add(3.5F);
                }
                yValues.add(1.0F);
            }

            var x = np.array(xValues.ToArray());
            var y = np.array(yValues.ToArray());
            x = x.reshape(10, 100);
            y = y.reshape(10, 1);

            var model = keras.Sequential(
                new List<ILayer>
                {
                    new Flatten(new FlattenArgs
                    {
                        BatchInputShape = new TensorShape(10, 100)
                    }),
                    keras.layers.Dense(100, activation: "relu"),
                    keras.layers.Dense(100, activation: "relu"),
                    keras.layers.Dense(100, activation: "softmax"),
                });

            model.compile(new SGD(0.1F), new SparseCategoricalCrossentropy(), new [] { "accuracy" });
            model.fit(x, y, 10, 100, 1);
        }

        public void Train(float[,] x_in, float[,] y_in)
        {
            var xValues = new List<float>();
            var yValues = new List<float>();
            for (var i = 0; i < 10; i++)
            {
                for (var ii = 0; ii < 100; ii++)
                {
                    xValues.add(3.5F);
                }
                yValues.add(1.0F);
            }

            var x = np.array(xValues.ToArray());
            var y = np.array(yValues.ToArray());
            x = x.reshape(10, 100);
            y = y.reshape(10, 1);

            var model = keras.Sequential(
                new List<ILayer>
                {
                    new Flatten(new FlattenArgs
                    {
                        BatchInputShape = new TensorShape(10, 100)
                    }),
                    keras.layers.Dense(100, activation: "relu"),
                    keras.layers.Dense(100, activation: "relu"),
                    keras.layers.Dense(100, activation: "softmax"),
                });

            model.compile(new SGD(0.1F), new SparseCategoricalCrossentropy(), new[] { "accuracy" });
            model.fit(x, y, 10, 100, 1);
        }
    }
}