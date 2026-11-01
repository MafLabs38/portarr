# 0012 — The Kubernetes connector reuses the node_exporter parser

- **Status**: accepted
- **Date**: 2026-11-01
- **Spec**: D-72

## Context

Host metrics are read from node_exporter's text format (D-72). A Docker host's exporter is reached over HTTP; a Kubernetes node's exporter can also be reached through the API server's pod proxy, which needs the Kubernetes connector's credentials and routing. Both fetch the same text, and the sampler that turns it into CPU, memory and swap (two reads of a counter, the memory split) is the part with the contract suite and the tests.

## Decision

`Portarr.Providers.Kubernetes` references `Portarr.Providers.NodeExporter` and reuses its sampler and parser. It is the only provider-to-provider reference: the Kubernetes connector owns how the text is fetched (pod lookup, proxy path, token), the node_exporter connector owns what the text means. Core does not know either.

## Consequences

- One parser, one set of tests, one memory cap on what a hostile exporter can send.
- A change to the sampler reaches both connectors, and both run the `HostMetricsSource` contract suite.
- The exit criterion: when a third connector needs the parser, it moves to a shared Prometheus-text library that both providers reference, and this reference goes away.
