﻿using System;
using System.Collections.Generic;
using Maestro;

namespace WorkflowConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var p = new Process("flow.bpmn");
            var processInstance = p.NewProcessInstance();
            processInstance.SetDefaultHandlers();
            processInstance.SetHandler("task", new MyTaskHandler());
            processInstance.SetHandler("startEvent", new MyStartHandler());

            var processVar = new Dictionary<string, object>() { { "processVar1", "value" }, { "processVar2", 50 } };
            processInstance.Start(processVar);
            Console.ReadLine();
        }

        private class MyStartHandler : INodeHandler
        {
            public void Execute(ProcessNode currentNode, ProcessNode previousNode)
            {
                Console.WriteLine("Custom Start Handler");
                Console.WriteLine(currentNode.NodeName);
                currentNode.Done();
            }
        }

        private class MyTaskHandler : INodeHandler
        {
            public void Execute(ProcessNode currentNode, ProcessNode previousNode)
            {
                Console.WriteLine("Custom Task Handler");
                Console.WriteLine(currentNode.NodeName);
                currentNode.Done();
            }
        }


    }
}
