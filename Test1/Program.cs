using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Maestro;

namespace Test1
{
    class Program
    {
        static void Main(string[] args)
        {
            //string modelFilename = "../../testdata/UserTaskFoo-c.bpmn";
            string modelFilename = "../../testdata/flow.bpmn"; 
            Console.WriteLine("Model: " + modelFilename);
            Console.WriteLine("READSTART: " + modelFilename);
            var model = new ProcessModel(File.OpenRead(modelFilename));
            Console.WriteLine("READEND: " + modelFilename);
            Console.WriteLine("INITSTART: " + modelFilename);
            var processInstance = model.ProcessInstance();
            Console.WriteLine("INITEND: " + modelFilename);
            processInstance.SetDefaultHandlers();
            processInstance.SetHandler("task", new MyTaskHandler());
            processInstance.SetHandler("startEvent", new MyStartHandler());

            var processInstanceInputParameters = new Dictionary<string, object>() { { "processVar1", "value1" }, { "processVar2", 50 } };

            Console.WriteLine("DUMPSTART: " + modelFilename);
            int nNodes = processInstance.Nodes.Count;
            Console.WriteLine("NodeDump: " + nNodes.ToString());
            foreach (string k in processInstance.Nodes.Keys)
            {
                ProcessNode n = processInstance.Nodes[k];
                Console.WriteLine(n.NodeSerialNumber.ToString()
                    + "\t" + k
                    + "\t" + n.NodeName
                    + "\t" + n.NodeType
                    + "\t" + ((n.PreviousNodes != null) ? n.PreviousNodes.Count.ToString() : "(null)")
                    + "\t" + ((n.NextNodes != null) ? n.NextNodes.Count.ToString() : "(null)")
                    + "\t" + ((n.InputParameters != null) ? n.InputParameters.Count.ToString() : "(null)")
                    + "\t" + ((n.OutputParameters != null) ? n.OutputParameters.Count.ToString() : "(null)")
                    + "\t" + ((n.Expression != null) ? n.Expression : "(null)")
                    );
            }

            List<string> keys = new List<string>();
            int[] ids = new int[nNodes + 1];
            foreach (string k in processInstance.Nodes.Keys) keys.Add(k);
            int nIDs = 0;
            foreach (string k in processInstance.Nodes.Keys) ids[processInstance.Nodes[k].NodeSerialNumber] = nIDs++;

            Console.WriteLine("NodeKeys: " + nNodes.ToString());
            for (int i = 1; i <= nNodes; i++)
            {
                int id = ids[i];
                string k = keys[id];
                Console.WriteLine(i.ToString() + "\t" + id.ToString() + "\t" + k);
            }

            Console.WriteLine("NodeDump (sorted): " + nNodes.ToString());
            for (int i = 1; i <= nNodes; i++)
            {
                int id = ids[i];
                string k = keys[id];
                ProcessNode n = processInstance.Nodes[k];
                Console.WriteLine(n.NodeSerialNumber.ToString() 
                    + "\t" + k 
                    + "\t" + n.NodeName 
                    + "\t" + n.NodeType
                    + "\t" + ((n.PreviousNodes != null) ? n.PreviousNodes.Count.ToString() : "(null)")
                    + "\t" + ((n.NextNodes != null) ? n.NextNodes.Count.ToString() : "(null)")
                    + "\t" + ((n.InputParameters != null) ? n.InputParameters.Count.ToString()  : "(null)")
                    + "\t" + ((n.OutputParameters != null) ? n.OutputParameters.Count.ToString() : "(null)")
                    + "\t" + ((n.Expression != null) ? n.Expression : "(null)")
                    );
            }
            Console.WriteLine("DUMPEND: " + modelFilename);

            Console.WriteLine("Press ENTER to see BPMV byecode...");
            Console.ReadLine();

            processInstance.Serialize1ProcessTemplate();

            //Console.WriteLine("Count:\t[" + processInstance.Nodes.Count.ToString() + "]");
            //foreach (string k in processInstance.Nodes.Keys)
            //{
            //    ProcessNode n = processInstance.Nodes[k];
            //    Console.WriteLine("[" + n.NodeID.ToString() + "]"
            //        + "\t" + k 
            //        + "\t" + n.NodeName 
            //        + "\t" + n.NodeType
            //        + "\t" + ((n.PreviousNodes != null) ? n.PreviousNodes.Count.ToString() : "(null)")
            //        + "\t" + ((n.NextNodes != null) ? n.NextNodes.Count.ToString() : "(null)")
            //        + "\t" + ((n.InputParameters != null) ? n.InputParameters.Count.ToString() : "(null)")
            //        + "\t" + ((n.OutputParameters != null) ? n.OutputParameters.Count.ToString() : "(null)")
            //        + "\t" + ((n.Expression != null) ? n.Expression : "(null)")
            //        );
            //}

            Console.WriteLine("Press ENTER to start process off-chain...");
            Console.ReadLine();

            processInstance.Serialize1ProcessInstance();
            Console.WriteLine("EXECSTART: " + modelFilename);
            processInstance.Start(processInstanceInputParameters);
            Console.WriteLine("EXECEND: " + modelFilename);
            Console.WriteLine("Press ENTER after completion of process off-chain processing");
            Console.ReadLine();
            processInstance.Serialize1ProcessInstance();

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }

        private class MyStartHandler : INodeHandler
        {
            public void Execute(ProcessNode currentNode, ProcessNode previousNode)
            {
                Console.WriteLine("*** Custom Start Handler");
                Console.WriteLine("*** " + currentNode.NodeName);
                currentNode.Done();
            }
        }

        private class MyTaskHandler : INodeHandler
        {
            public void Execute(ProcessNode currentNode, ProcessNode previousNode)
            {
                Console.WriteLine("*** Custom Task Handler");
                Console.WriteLine("*** " + currentNode.NodeName);
                currentNode.Done();
            }
        }
    }
}