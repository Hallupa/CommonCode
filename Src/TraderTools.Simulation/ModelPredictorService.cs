using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using System.Threading;
using Hallupa.Library;
using Keras.Models;
using log4net;
using Numpy;
using Numpy.Models;
using Python.Runtime;

namespace TraderTools.Simulation
{
    public interface IModelDetails
    {
    }

    public class ModelDetails : IModelDetails
    {
        public BaseModel BaseModel { get; set; }
    }

    [Export(typeof(ModelPredictorService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class ModelPredictorService
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private Py.GILState _y;
        private Dictionary<string, ModelDetails> _modelsLookup = new Dictionary<string, ModelDetails>();
        private ProducerConsumer<Action> _producerConsumer;

        public ModelPredictorService()
        {
            _producerConsumer = new ProducerConsumer<Action>(1, ConsumeAction);
            _producerConsumer.Add(() => _y = Py.GIL());
            _producerConsumer.Start();
        }

        private ProducerConsumerActionResult ConsumeAction(Action arg)
        {
            try
            {
                arg();
            }
            catch (Exception ex)
            {
                Log.Error("Error processing", ex);
            }

            return ProducerConsumerActionResult.Success;
        }

        public int Predict(IModelDetails modelDetails, params float[] xValues)
        {
            var yValue = -1;
            ManualResetEvent wait = new ManualResetEvent(false);
            _producerConsumer.Add(() =>
            {
                var x = np.array(np.array(xValues)).reshape(new Shape(1, xValues.Length));

                var y = ((ModelDetails)modelDetails).BaseModel.Predict(x)[0];

                // Get which index is highest
                for (var i = 0; i < y.size; i++)
                {
                    if ((float)y[i] > 0.9 && (yValue == -1 || (float)y[i] > (float)y[yValue])) yValue = i;
                }

                wait.Set();
            });

            wait.WaitOne();
            return yValue;
        }

        public IModelDetails LoadModel(string path)
        {
            lock (_modelsLookup)
            {
                if (_modelsLookup.ContainsKey(path))
                {
                    return _modelsLookup[path];
                }

                BaseModel baseModel = null;
                var waitEvent = new ManualResetEvent(false);
                _producerConsumer.Add(() =>
                {
                    Log.Info($"Loading model: {Path.GetFileName(path)}");
                    baseModel = BaseModel.LoadModel(path);
                    Log.Info($"Model loaded");
                    waitEvent.Set();
                });

                waitEvent.WaitOne();
                var ret = new ModelDetails
                {
                    BaseModel = baseModel
                };
                _modelsLookup[path] = ret;
                return ret;
            }
        }
    }
}