namespace ChaosExperimentRunner.Models;

public enum ProbeType { Http, Process, Command }

public enum FaultType { HttpLatency, HttpError, ProcessTerminate, ResourceExhaustion }

public enum ExperimentStatus { Passed, Failed, Aborted }

public enum ResourceType { Cpu, Memory }
