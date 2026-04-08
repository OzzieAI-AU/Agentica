# 🚀 OzzieAI Agentica: The Autonomous Swarm Framework

**OzzieAI Agentica** is an industrial-grade, multi-agent orchestration framework designed for high-autonomy project execution. Moving beyond simple "chatbots," Agentica creates a digital organism—a hierarchical swarm of independent "brains" that communicate via an asynchronous bus to research, architect, code, and verify complex software systems.

For the latest updates, official source code, and enterprise support, visit [OzzieAI Official](https://www.ozzieai.com) or join the community at the [OzzieAI Forum](https://forum.ozzieai.com).

---

## 🏗️ I. The Philosophy: The "Unison" Protocol

Traditional AI agents often suffer from "Context Drift" and "Reasoning Bottlenecks." Agentica solves this through the **Unison Protocol**, which divides labor based on cognitive complexity.

### 1. The Hierarchical Swarm
To ensure high-fidelity execution, we divide responsibilities into three distinct tiers:
* **Boss Agent (The Architect):** Uses high-reasoning models (e.g., Grok-2, Gemini 1.5 Pro) to define the mission, validate ethical constraints, and perform final code reviews.
* **Manager Agent (The Orchestrator):** Breaks down the Boss's vision into a sequence of technical tasks. It manages the lifecycle of Worker agents and synthesizes their technical reports.
* **Worker Agent (The Engine):** Executes low-level technical tasks (coding, searching, compiling). Optimized for fast, specialized models (e.g., Ollama/Llama-3-Coder) to ensure rapid iteration.

![Agentica Hierarchical Swarm Architecture](Images/img01.png)

### 2. Cognitive Diversity (Multi-Brain Support)
Unlike single-model systems, Agentica allows every agent to have its own unique LLM provider. You can run a "Premium" brain for the Boss and "Local/Open-Source" brains for Workers to balance cost, privacy, and performance.

---

## 🧠 II. DragonMemory™: The Multi-Tiered Neural Core

Agentica uses a proprietary memory architecture called **DragonMemory**, which mimics human cognitive layers to ensure that agents learn from their mistakes and remember their successes.

### 1. Tier 1: Ephemeral Memory (The "Conscious" Stream)
Stored in the agent's active `History`, this represents the immediate task context. It is a sliding-window memory that ensures the agent remains focused on the current objective without being overwhelmed by past noise.

### 2. Tier 2: Tactical Memory (Skill & Fact Propagation)
When a Worker discovers a specific solution (e.g., "The correct MSBuild flag for a C++ DLL"), it triggers the `LearnSkill` method. Through **Upstream Propagation**, this fact "bubbles up" to the Manager and Boss, ensuring the entire swarm "knows" what the Worker has learned without manual re-briefing.

### 3. Tier 3: Persistent Memory (The Hardened "Cortex")
Powered by the `LiveCache` engine, this layer hardens knowledge to the physical disk. Every file generated is assigned a **Perfection Score (0-100)**. 
* **Integrity:** Files are only saved if they pass the `CodeSafetyGate`.
* **Persistence:** Knowledge survives system reboots by serializing into `LiveCache.json`.

![DragonMemory Tiered Storage Logic](Images/img02.png)

---

## ⚙️ III. The Cognitive Execution Loop

Every agent operates in a non-blocking, recursive **Think-Act-Observe** cycle. This loop allows the agent to self-correct in real-time.

1.  **Ingestion:** The agent receives a mission via the `AgentBus`.
2.  **Deliberation (Think):** The agent analyzes the project structure using the `ProjectAnalyzerTool`.
3.  **Execution (Act):** The agent selects and runs a tool (e.g., writing a file via `FileToolExecutor`).
4.  **Verification (Observe):** The result of the action (success or error) is fed back into memory. If a build fails, the agent automatically interprets the error and attempts a fix.

![The Autonomous Reasoning and Verification Loop](Images/img03.png)

---

## 🛠️ IV. Built-in Tooling (The Hands)

Agentica agents come pre-equipped with a suite of professional tools:
* **FileToolExecutor:** High-speed I/O for reading, writing, and listing files.
* **TerminalTool:** Provides agents with shell access (`cmd.exe` or `bash`) to run compilers or git commands.
* **CodeSafetyGateTool:** A Roslyn-powered validator that checks syntax before persistence.
* **ProjectAnalyzerTool:** Generates a recursive map of the codebase for high-level context.
* **WebSearchTool:** Grants agents real-time access to documentation and troubleshooting data.

---

## 🚀 V. Advanced Implementation (Multi-Brain Setup)

One of Agentica's greatest strengths is its ability to assign different LLM providers to different roles.

```C#

Console.Title = "OzzieAI Agentica Framework";
        Console.WriteLine("🤖 Initializing Agentica Swarm...");

        // Infrastructure Setup
        string projectPath = Path.Combine(Directory.GetCurrentDirectory(), "Workspace");

        // 1. Initialize different providers
        var geminiKey = Environment.GetEnvironmentVariable("Gemini_API_KEY") ?? throw new Exception("Gemini_API_KEY not set in environment variables.");
        var grokKey = Environment.GetEnvironmentVariable("GROK_API_KEY") ?? throw new Exception("GROK_API_KEY not set in environment variables.");
        var geminiBrain = new GeminiProvider(apiKey: geminiKey, model: "gemini-flash-latest");
        var grokBrain = new GrokProvider(apiKey: grokKey, model: "grok-4.20-0309-reasoning");
        var ollamaBrain = new OllamaProvider(model: "codellama:7b");

        // 2. Setup Factory (No global brain anymore)
        var bus = new AgentBus();
        var factory = new AgentFactory(bus, new LiveCache(projectPath));

        // 3. Create the Swarm with independent brains
        var boss = (BossAgent)factory.CreateAgent(new AgentConfig
        {
            Name = "Ozzie-CEO",
            Role = AgentRole.Boss,
            Provider = grokBrain
        });

        var manager = (ManagerAgent)factory.CreateAgent(new AgentConfig
        {
            Name = "PM-Alice",
            Role = AgentRole.Manager,
            Provider = grokBrain
        });

        var worker = (WorkerAgent)factory.CreateAgent(new AgentConfig
        {
            Name = "Bob-Dev",
            Role = AgentRole.Worker,
            Provider = ollamaBrain
        });

        // 4. Arming the Worker with your Uploaded Tools
        worker.AddTool(new FileToolExecutor());     // The 'Hands'
        worker.AddTool(new TerminalTool());         // The 'Action'
        worker.AddTool(new CodeSafetyGateTool());   // The 'Guard'
        worker.AddTool(new WebSearchTool());        // The 'Eyes'

        // Give the Manager 'Sight'
        manager.AddTool(new ProjectAnalyzerTool());
        manager.AssignWorker(worker.Config.Id);

        // 5. The Mission
        string mission = "Research best practices for a C# 64-bit sum implementation, " +
                         "create the file, and verify it with a terminal build command.";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[MISSION START]: {mission}\n");
        Console.ResetColor();

        // Start the chain: Boss -> Manager
        await bus.SendAsync(new AgentMessage(
            boss.Config.Id,
            manager.Config.Id,
            MessageType.TaskAssignment,
            mission,
            DateTime.UtcNow));

        // Keep the process alive
        while (true) { await Task.Delay(1000); }
        
```

---

## 🌐 Contact & Support
* **Official Website:** [ozzieai.com](https://www.ozzieai.com)
* **Developer Forum:** [forum.ozzieai.com](https://forum.ozzieai.com)