﻿using Microsoft.CodeAnalysis.CSharp.Scripting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using t = System.Threading.Tasks;

namespace Maestro
{
    public class ProcessModel
    {
        internal IEnumerable<Property> Properties { get; set; }
        public XElement ProcessXML { get; set; }
        public XNamespace NS { get; set; }
        private ProcessModel()
        {
        }

        public ProcessModel(Stream bpmnStream)
        {
            XDocument doc = XDocument.Load(bpmnStream);
            NS = @"http://www.omg.org/spec/BPMN/20100524/MODEL";
            ProcessXML = doc.Root.Element(NS + "process");
            Properties = PropertyInitializer(ProcessXML, NS);
        }

        public ProcessInstance ProcessInstance()
        {
            var elementStartEvent = ProcessXML.Element(NS + "startEvent");
            //var nodeStart = new ProcessNode(current.Attribute("id").Value, current.Name.LocalName);
            var nodes = BuildNodes(ProcessXML);
            var nodeStart = nodes[elementStartEvent.Attribute("id").Value];
            var processInstance = new ProcessInstance(this);
            //BuildLinkedNodes0(current, ref nodeStart, nodes, processInstance);
            BuildLinkedNodes1(ProcessXML, nodes, processInstance);
            processInstance.Id = Guid.NewGuid().ToString();
            processInstance.StartNode = nodeStart;
            processInstance.Nodes = nodes.ToImmutableDictionary();

            return processInstance;
        }

        private IDictionary<string, ProcessNode> BuildNodes(XElement processXML)
        {
            //var nodes = processXML.Elements().ToDictionary(e => e.Attribute("id").Value, e => new ProcessNode(e.Attribute("id").Value, e.Name.LocalName));
            var nodes = new Dictionary<string, ProcessNode>();
            var elements = processXML.Elements();
            Console.WriteLine("elements.Count:\t" + elements.Count().ToString());
            foreach(var element in elements)
            {
                //Console.WriteLine(element.Name + " " + element.Name.LocalName + " " + element.Value);
                //var attrs = element.Attributes();
                //foreach(var attr in attrs)
                //{
                //    Console.WriteLine(attr.Name + " " + attr.Value);
                //}
                string name = element.Name.LocalName;
                switch(name)
                {
                    case "sequenceFlow":
                        {
                            string targetRef = (element.Attribute("targetRef") != null ? element.Attribute("targetRef").Value : "(null)");
                            Console.WriteLine("SEQ  Supported: " + name + "\t" + element.Attribute("id").Value + "\t" + targetRef);
                            ProcessNode pn = new ProcessNode(element.Attribute("id").Value, name);
                            nodes.Add(element.Attribute("id").Value, pn);
                            break;
                        }
                    case "startEvent":
                    case "endEvent":
                    case "task":
                    case "userTask":
                    case "serviceTask":
                    case "scriptTask":
                    case "businessRuleTask":
                    case "scriptRuleTask":
                        {
                            string targetRef = (element.Attribute("targetRef") != null ? element.Attribute("targetRef").Value : "(null)");
                            Console.WriteLine("TASK Supported: " + name + "\t" + element.Attribute("id").Value + "\t" + targetRef);
                            ProcessNode pn = new ProcessNode(element.Attribute("id").Value, name);
                            nodes.Add(element.Attribute("id").Value, pn);
                            break;
                        }
                    case "exclusiveGateway":
                    case "inclusiveGateway":
                        {
                            string targetRef = (element.Attribute("targetRef") != null ? element.Attribute("targetRef").Value : "(null)");
                            Console.WriteLine("GATE Supported: " + name + "\t" + element.Attribute("id").Value + "\t" + targetRef);
                            ProcessNode pn = new ProcessNode(element.Attribute("id").Value, name);
                            nodes.Add(element.Attribute("id").Value, pn);
                            break;
                        }
                    case "property":
                    case "dataAssociation":
                    case "extensionElements":
                    case "participant":
                    case "laneSet":
                    case "messageFlow":
                    case "parallelGateway":
                    case "subProcess":
                    case "callActivity":
                    case "DataObject":
                    case "TextAnnotation":
                    case "assocation":
                    case "dataStoreReference":
                        {
                            Console.WriteLine("Not supported: " + element.Name);
                            break;
                        }
                    default:
                        {
                            Console.WriteLine("Unknown: " + element.Name);
                            break;
                        }
                }
            }
            nodes.Where(e => e.Value.NodeType == "property").Select(e => e.Key).ToList().ForEach(k => nodes.Remove(k));
            var scripts = processXML.Elements().Elements(NS + "script")
                .Select(s => new { id = s.Parent.Attribute("id").Value, expression = s.Value });
            foreach (var s in scripts) nodes[s.id].Expression = s.expression;

            var conditionExpressions = processXML.Elements().Elements(NS + "conditionExpression")
                .Select(c => new { id = c.Parent.Attribute("id").Value, expression = c.Value });
            foreach (var c in conditionExpressions) nodes[c.id].Expression = c.expression;

            //Quick fix for zmq example
            //TODO Proper process var/assignment to node var mapping
            var taskExpressions = processXML.Elements(NS + "task").Elements(NS + "dataInputAssociation").Elements(NS + "assignment").Elements(NS + "from")
                .Select(e => new { id = e.Parent.Parent.Parent.Attribute("id").Value, expression = e.Value });
            foreach (var e in taskExpressions) nodes[e.id].Expression = e.expression;

            return nodes;
        }

        private Func<XElement, XElement, XNamespace, IEnumerable<XElement>> NextSequences =
            (e, ProcessXML, NS) => ProcessXML.Elements(NS + "sequenceFlow")?
            .Where(s => s.Attribute("sourceRef")?.Value == e.Attribute("id").Value);

        private Func<XElement, XElement, IEnumerable<XElement>> NextElement =
            (s, ProcessXML) => ProcessXML.Elements()
            .Where(e => e.Attribute("id").Value == s.Attribute("targetRef")?.Value);

        private void BuildLinkedNodes0(XElement elementCurrent, ref ProcessNode nodeCurrent, IDictionary<string, ProcessNode> nodes, ProcessInstance processInstance)
        {
            Console.WriteLine("nodeCurrent: " + nodeCurrent.NodeName + " " + nodeCurrent.NodeType);
            nodeCurrent.ProcessInstance = processInstance;
            var seq = NextSequences(elementCurrent, ProcessXML, NS);
            var nextElements = (seq.Any() ? seq : NextElement(elementCurrent, ProcessXML));
            nodeCurrent.NextNodes = new List<ProcessNode>();
            
            foreach (var elementNext in nextElements)
            {
                Console.WriteLine("n: " + elementNext.Attribute("id").Value + " " + elementNext.Name.LocalName);

                var nextNode = nodes[elementNext.Attribute("id").Value];
                nodeCurrent.NextNodes.Add(nextNode);

                if (nextNode.PreviousNodes == null) nextNode.PreviousNodes = new List<ProcessNode>();
                if (!nextNode.PreviousNodes.Contains(nodeCurrent)) nextNode.PreviousNodes.Add(nodeCurrent);

                BuildLinkedNodes0(elementNext, ref nextNode, nodes, processInstance);
            }
        }

        private void BuildLinkedNodes1(XElement ProcessXML, IDictionary<string, ProcessNode> nodes, ProcessInstance processInstance)
        {
            var elements = ProcessXML.Elements();
            Console.WriteLine("elements.Count:\t" + elements.Count().ToString());
            foreach (var element in elements)
            {
                string name = element.Name.LocalName;
                switch (name)
                {
                    case "sequenceFlow":
                        {
                            string targetRef = (element.Attribute("targetRef") != null ? element.Attribute("targetRef").Value : "");
                            string sourceRef = (element.Attribute("sourceRef") != null ? element.Attribute("sourceRef").Value : "");

                            Console.WriteLine("SEQ  References: " + name + "\t" + element.Attribute("id").Value + "\t" + sourceRef + "\t" + targetRef);

                            var currentNode = nodes[element.Attribute("id").Value];
                            var sourceNode = nodes[sourceRef];
                            var targetNode = nodes[targetRef];

                            if (currentNode.PreviousNodes == null) currentNode.PreviousNodes = new List<ProcessNode>();
                            if (currentNode.NextNodes == null) currentNode.NextNodes = new List<ProcessNode>();
                            if (!currentNode.PreviousNodes.Contains(sourceNode)) currentNode.PreviousNodes.Add(sourceNode);
                            if (!currentNode.NextNodes.Contains(targetNode)) currentNode.NextNodes.Add(targetNode);

                            if (sourceNode.NextNodes == null) sourceNode.NextNodes = new List<ProcessNode>();
                            if (!sourceNode.NextNodes.Contains(currentNode)) sourceNode.NextNodes.Add(currentNode);

                            if (targetNode.PreviousNodes == null) targetNode.PreviousNodes = new List<ProcessNode>();
                            if (!targetNode.PreviousNodes.Contains(currentNode)) targetNode.PreviousNodes.Add(currentNode);

                            break;
                        }
                    default:
                        {
                            break;
                        }
                }
            }

            foreach (ProcessNode pn in nodes.Values)
            {
                Console.WriteLine("pn.ProcessInstance: " + pn.NodeName);
                pn.ProcessInstance = processInstance;
            }
        }

        internal string GetAssociation(string nodeId, string nodeVariableName)
        {
            var node = ProcessXML.Elements().Where(e => e.Attribute("id").Value == nodeId);
            var inputId = node.Elements(NS + "ioSpecification").Elements(NS + "dataInput")
                .Where(e => e.Attribute("name").Value == nodeVariableName).FirstOrDefault().Attribute("id").Value;
            var propertyId = node.Elements(NS + "dataInputAssociation")
                .Where(d => d.Element(NS + "targetRef").Value == inputId).Elements(NS + "sourceRef").FirstOrDefault().Value;
            var propertyName = ProcessXML.Elements(NS + "property")
                .Where(e => e.Attribute("id").Value == propertyId).Attributes("name").FirstOrDefault().Value;
            return propertyName;
        }

        private IEnumerable<Property> PropertyInitializer(XElement process, XNamespace ns)
        {
            var itemDefinitions = process.Parent.Elements(ns + "itemDefinition");
            var properties = process.Elements(ns + "property").ToList();
            var propertyList = new List<Property>();
            foreach (var property in properties)
            {
                string id = property.Attribute("id").Value;
                string name = property.Attribute("name").Value;
                string itemSubjectRef = property.Attribute("itemSubjectRef").Value;
                string structureRef = itemDefinitions
                    .Where(i => i.Attribute("id").Value == itemSubjectRef)
                    .FirstOrDefault()
                    .Attribute("structureRef")
                    .Value;
                bool isCollection = Convert.ToBoolean(itemDefinitions
                    .Where(i => i.Attribute("id").Value == itemSubjectRef)
                    .FirstOrDefault()
                    .Attribute("isCollection")
                    .Value);
                propertyList.Add(new Property(id, name, structureRef, isCollection));
            }

            return propertyList;
        }
    }

    internal class Property
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string StructureRef { get; set; }
        public bool IsCollection { get; set; }

        public Property(string id, string name, string structureRef, bool isCollection)
        {
            Id = id;
            Name = name;
            StructureRef = structureRef;
            IsCollection = isCollection;
        }
    }

    public class ProcessInstance
    {
        public string Id { get; set; }
        public ProcessModel Process { get; }
        private IImmutableDictionary<string, object> inputParameters;
        public IImmutableDictionary<string, object> InputParameters
        {
            get
            {
                return inputParameters;
            }

            set
            {
                if (ValidParameters(value))
                    inputParameters = value;
                else
                    throw new Exception("Parameter type does not match process definition");
            }
        }
        public IImmutableDictionary<string, IImmutableDictionary<string, object>> OutputParameters { get; set; }
        public ProcessNode StartNode { get; internal set; }
        public IImmutableDictionary<string, ProcessNode> Nodes { get; set; }
        private IDictionary<string, INodeHandler> nodeHandlers;
        public IDictionary<string, INodeHandler> NodeHandlers
        {
            get
            {
                return nodeHandlers;
            }

            set
            {
                if (ValidHandlers(value))
                    nodeHandlers = value;
                else
                    throw new Exception("Unhandled node type");
            }
        }

        public ProcessInstance(ProcessModel process)
        {
            Process = process;
        }

        public List<string> NodeTypes = new List<string>();
        public void Serialize1ProcessTemplate()
        {
            List<string> keys = new List<string>();
            int[] ids = new int[this.Nodes.Count + 1];
            foreach (string k in this.Nodes.Keys) keys.Add(k);
            int nIDs = 0;
            foreach (string k in this.Nodes.Keys) ids[this.Nodes[k].NodeSerialNumber] = nIDs++;

            foreach (string k in this.Nodes.Keys)
            {
                ProcessNode n = this.Nodes[k];
                if (!NodeTypes.Contains(n.NodeType)) NodeTypes.Add(n.NodeType);
                n.NodeTypeSerialNumber = NodeTypes.IndexOf(n.NodeType);
            }

            Console.WriteLine("NodeTypes:\t[" + NodeTypes.Count.ToString() + "]");
            foreach (string t in NodeTypes)
            {
                Console.WriteLine("[" + NodeTypes.IndexOf(t) + "]"
                    + "\t" + "[" + t + "]"
                );
            }

            Console.WriteLine("NodeNames:\t[" + this.Nodes.Count.ToString() + "]");
            for (int i = 1; i <= this.Nodes.Count; i++)
            {
                int id = ids[i];
                string k = keys[id];
                ProcessNode n = this.Nodes[k];
                Console.WriteLine("[" + n.NodeSerialNumber.ToString() + "]"
                    + "\t" + "[" + n.NodeName + "]"
                );
            }

            Console.WriteLine("NodeCodeGraph:\t[" + this.Nodes.Count.ToString() + "]");
            for (int i = 1; i <= this.Nodes.Count; i++)
            {
                int id = ids[i];
                string k = keys[id];
                ProcessNode n = this.Nodes[k];
                Console.Write("[" + n.NodeSerialNumber.ToString() + "]"
                    + "\t" + "[" + n.NodeTypeSerialNumber.ToString() + "]"
                );
                Console.Write(",");
                Console.Write("[");
                if (n.PreviousNodes != null)
                {
                    bool firstItem2 = true;
                    foreach (ProcessNode pn in n.PreviousNodes)
                    {
                        if (!firstItem2) Console.Write(","); firstItem2 = false;
                        Console.Write(pn.NodeSerialNumber.ToString());
                    }
                }
                Console.Write("]");
                Console.Write(",");
                Console.Write("[");
                if (n.NextNodes != null)
                {
                    bool firstItem2 = true;
                    foreach (ProcessNode nn in n.NextNodes)
                    {
                        if (!firstItem2) Console.Write(","); firstItem2 = false;
                        Console.Write(nn.NodeSerialNumber.ToString());
                    }
                }
                Console.Write("]");
                Console.WriteLine();
            }

            Console.WriteLine("NodeIEO:\t[" + this.Nodes.Count.ToString() + "]");
            for (int i = 1; i <= this.Nodes.Count; i++)
            {
                int id = ids[i];
                string k = keys[id];
                ProcessNode n = this.Nodes[k];
                //fConsole.Write("Node,IEO: ");
                Console.Write("[" + n.NodeSerialNumber.ToString() + "]"
                );
                Console.Write(",");
                Serialize1IOParameters(n.InputParameters);
                Console.Write(",");
                Serialize1Expression(n.Expression);
                Console.Write(",");
                Serialize1IOParameters(n.OutputParameters);
                Console.WriteLine();
            }
        }

        public void Serialize1ProcessInstance()
        {

            Console.WriteLine("ProcessInstance:START");
            Console.WriteLine("[" + this.Id + "]"
                );

            Console.WriteLine("ProcessInstanceIEO:");
            Serialize1IOParameters(this.InputParameters);
            Console.Write(",");
            Console.Write("[");
            bool firstItem = true;
            if (this.OutputParameters != null) foreach(var op in this.OutputParameters)
            {
                if (!firstItem) Console.Write(","); firstItem = false;
                Serialize1IOParameters(op.Value);
            }
            Console.Write("]");
            Console.WriteLine();

            Console.WriteLine("StartNode:");
            Serialize1StartNode(this.StartNode);
            Console.WriteLine();
            Console.WriteLine("ProcessInstance:END");
        }

        public void Serialize1StartNode(ProcessNode nodeStart)
        {
            Console.WriteLine("[" + nodeStart.NodeSerialNumber.ToString() + "]"
                    + "\t" + "[" + nodeStart.NodeName + "]"
                    );
            Console.Write("IO: ");
            Serialize1IOParameters(nodeStart.InputParameters);
            Console.Write(",");
            Serialize1IOParameters(nodeStart.OutputParameters);
        }

        public void Serialize1Expression(string expression)
        {
            Console.Write("[");
            if (expression != null) Console.Write(expression);
            Console.Write("]");
        }

        public void Serialize1IOParameters(IImmutableDictionary<string, object> parameters)
        {
            Serialize1IOParameters(parameters, false);
        }
        public void Serialize1IOParameters(IImmutableDictionary<string, object> parameters, bool fNewline)
        {
            Console.Write("[");
            if (parameters != null)
            {
                bool firstItem = true;
                foreach (var p in parameters)
                {
                    if (!firstItem) Console.Write(","); firstItem = false;
                    Console.Write(p.Key + "=" + p.Value.ToString());
                }
            }
            Console.Write("]");
            if (fNewline) Console.WriteLine();
        }

        public void Start()
        {
            StartNode.Execute(StartNode, null);
        }

        public void SetDefaultHandlers()
        {
            var defaultNodeHandlers = new Dictionary<string, INodeHandler>()
            {
                { "startEvent", new DefaultStartHandler()},
                { "endEvent", new DefaultEndHandler()},
                { "task", new DefaultTaskHandler()},
                { "userTask", new DefaultTaskHandler()}, // mwh
                { "sequenceFlow", new DefaultSequenceHandler()},
                { "businessRuleTask", new DefaultBusinessRuleHandler()},
                { "exclusiveGateway", new DefaultExclusiveGatewayHandler()},
                { "inclusiveGateway", new DefaultInclusiveGatewayHandler()},
                { "scriptTask", new DefaultScriptTaskHandler()}
            };

            if (Nodes.All(t => defaultNodeHandlers.ContainsKey(t.Value.NodeType)))
            {
                nodeHandlers = new Dictionary<string, INodeHandler>();
                foreach (string n in Nodes.Values.Select(n => n.NodeType).Distinct())
                {
                    nodeHandlers.Add(n, defaultNodeHandlers[n]);
                }
            }
            else
                throw new Exception("Process contains an unknown node type");
        }

        public void SetHandler(string nodeType, INodeHandler nodeHandler)
        {
            if (nodeHandlers == null)
                nodeHandlers = new Dictionary<string, INodeHandler>();

            if (nodeHandlers.ContainsKey(nodeType))
                nodeHandlers[nodeType] = nodeHandler;
            else
                nodeHandlers.Add(nodeType, nodeHandler);
        }

        private bool ValidHandlers(IDictionary<string, INodeHandler> handlers)
        {
            var nodeTypes = Nodes.Values.Select(n => n.NodeType).Distinct();
            return nodeTypes.All(t => handlers.Keys.Contains(t));
        }

        private bool ValidParameters(IImmutableDictionary<string, object> parameters)
        {
            var propertyMap = Process.Properties.ToDictionary(p => p.Name, p => p.StructureRef);
            return parameters.All(p => p.Value.GetType().Name.ToLower() == propertyMap[p.Key].ToLower());
        }

        public void Start(IDictionary<string, object> processInstanceInputParameters)
        {
            //TODO Get node variables not process instance var
            InputParameters = processInstanceInputParameters.ToImmutableDictionary();
            StartNode.InputParameters = processInstanceInputParameters.ToImmutableDictionary();
            Start();
        }

        internal void SetOutputParameters(ProcessNode node)
        {
            if (OutputParameters == null)
            {
                OutputParameters = ImmutableDictionary.Create<string, IImmutableDictionary<string, object>>();
            }

            OutputParameters.Add(node.NodeName, node.OutputParameters);
        }
    }

    public interface INodeHandler
    {
        void Execute(ProcessNode currentNode, ProcessNode previousNode);
    }

    public class ProcessNode
    {
        public int NodeSerialNumber { get; set; }
        public int NodeTypeSerialNumber { get; set; }
        public string NodeName { get; set; }
        public string NodeType { get; set; }
        public ProcessInstance ProcessInstance { get; set; }
        public IImmutableDictionary<string, object> InputParameters { get; set; }
        public IImmutableDictionary<string, object> OutputParameters { get; set; }
        public INodeHandler NodeHandler { get; set; }
        public ICollection<ProcessNode> NextNodes { get; set; }
        public ICollection<ProcessNode> PreviousNodes { get; set; }
        private t.Task Task { get; set; }
        public string Expression { get; set; }

        private static int nNodes = 0;

        public ProcessNode()
        {
        }

        public ProcessNode(INodeHandler nodeHandler)
        {
            NodeHandler = nodeHandler;
        }

        public ProcessNode(string name, string type)
        {
            NodeSerialNumber = nNodes; nNodes++;
            NodeName = name;
            NodeType = type;
        }

        public void Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            NodeHandler = ProcessInstance.NodeHandlers[NodeType];
            if (processNode.InputParameters == null) processNode.InputParameters = ProcessInstance.InputParameters;
            Task = new t.Task(() => NodeHandler.Execute(processNode, previousNode));
            Task.Start();
        }

        public void Done()
        {
            if (NextNodes != null)
            {
                int i = 0;
                foreach (var nodeNext in NextNodes)
                {
                    Console.WriteLine($"Done: {this.NodeName} nodeNext.{i}: {nodeNext.NodeSerialNumber.ToString()}\t{nodeNext.NodeName}\t{nodeNext.NodeType}");
                    //to replace with variable resolution
                    //for each node retrieve input parameters defined in BPMN
                    //retrieve from node.OutputParameters (results of previous node)
                    //retrieve missing necessary input from process variables
                    nodeNext.InputParameters = OutputParameters;
                    Console.Write("I: ");
                    nodeNext.ProcessInstance.Serialize1IOParameters(nodeNext.InputParameters, true);
                    nodeNext.Execute(nodeNext, this);
                    i++;
                }
            }
        }
    }

    internal class DefaultTaskHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing Task (default)");
            processNode.Done();
        }
    }

    internal class DefaultStartHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing Start (default)");
            processNode.Done();
        }
    }

    internal class DefaultExclusiveGatewayHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing Exclusive Gateway (default)");
            processNode.Done();
        }
    }

    internal class DefaultBusinessRuleHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing BusinessRule (default)");
            processNode.Done();
        }
    }

    internal class DefaultSequenceHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            bool result = true;
            if (processNode.Expression == null)
            {
                Console.WriteLine(processNode.NodeName + " Executing Sequence (default)");
            }
            else
            {
                Console.WriteLine(processNode.NodeName + " Executing Sequence (with Expression) (default)");
                Console.WriteLine("Condition expression: " + processNode.Expression);
                var globals = new Globals(processNode.InputParameters.ToDictionary(e => e.Key, e => e.Value));
                try
                {
                    result = CSharpScript.EvaluateAsync<bool>(processNode.Expression, globals: globals).Result;
                    Console.WriteLine("Condition result: " + result.ToString());
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }

            if (result)
            {
                processNode.Done();
            }
        }
    }

    internal class DefaultEndHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing End (default)");
            processNode.ProcessInstance.SetOutputParameters(processNode);
            processNode.Done();
        }
    }

    internal class DefaultScriptTaskHandler : INodeHandler
    {
        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing Script (default)");

            if (processNode.Expression != null)
            {
                Console.WriteLine("Script: " + processNode.Expression);
                var globals = new Globals(processNode.InputParameters.ToDictionary(e => e.Key, e => e.Value));
                try
                {
                    processNode.OutputParameters =
                        CSharpScript.EvaluateAsync<IDictionary<string, object>>(processNode.Expression, globals: globals)
                        .Result.ToImmutableDictionary();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            processNode.Done();
        }
    }

    public class Globals
    {
        public IDictionary<string, object> globals;
        public Globals(IDictionary<string, object> parameters)
        {
            globals = parameters;
        }
    }

    internal class DefaultInclusiveGatewayHandler : INodeHandler
    {
        ConcurrentDictionary<ProcessNode, ICollection<ProcessNode>> sequenceWait = new ConcurrentDictionary<ProcessNode, ICollection<ProcessNode>>();

        void INodeHandler.Execute(ProcessNode processNode, ProcessNode previousNode)
        {
            Console.WriteLine(processNode.NodeName + " Executing Inclusive Gateway (default)");
            sequenceWait.GetOrAdd(processNode, new List<ProcessNode>(processNode.PreviousNodes));
            lock (sequenceWait[processNode])
            {
                sequenceWait[processNode].Remove(previousNode);
            }
            if (sequenceWait[processNode].Count == 0)
            {
                processNode.Done();
            }
        }
    }
    
}


