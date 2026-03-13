# ArxisStudio.Markup

`ArxisStudio.Markup.Json.Generator` is not a general replacement for XAML. It is infrastructure for a visual Avalonia form designer where UI is stored as an editable object tree and then compiled into C# at build time.

The core idea is:

- XAML is a text markup language optimized for authoring views by hand.
- `.arxui` files in this repository are a serialized UI model optimized for tooling.
- A designer can add, remove, reorder, and update controls by mutating structured data instead of rewriting markup text.
- The source generator turns that model into `InitializeComponent()` code for the target partial class.

This makes the format suitable for:

- visual tree editing
- property inspector workflows
- drag-and-drop composition
- serialization/deserialization
- designer-time transformations
- code generation during build

## Repository Layout

- `ArxisStudio.Markup.Json`
  Contracts and serialization for the `.arxui` JSON document model.

- `ArxisStudio.Markup.Json.Generator`
  Incremental Roslyn source generator. Reads `.arxui` files from `AdditionalFiles`, resolves types and properties through Roslyn, and emits C# code that constructs the Avalonia visual tree.

- `ArxisStudio.Markup.Sample`
  Sample Avalonia application that consumes the generator. It demonstrates the intended workflow: a partial class plus a matching `.arxui` description.

- `ArxisStudio.Markup.Json.Loader`
  Experimental visual editor / previewer. It edits the JSON model as a tree and renders the result at runtime using reflection. This project demonstrates why the model is useful for tooling even outside build-time generation.

- `ArxisStudio.Markup.Json.Generator.Tests`
  Tests for generator behavior around the current `.arxui` schema.

## How It Works

1. A view is declared as a normal partial Avalonia class.
2. A matching `.arxui` file describes the control tree and property values.
3. The generator finds the matching partial class by file name.
4. The generator emits a partial class implementation with `InitializeComponent()`.
5. The generated code instantiates controls, assigns properties, binds Avalonia properties, and wires nested objects/collections.

## Why This Exists

The value of this approach is not "support every XAML feature".

The value is that the UI becomes a manipulable model:

- each control is a node
- each property is structured data
- the tree can be diffed and transformed
- tools can operate on it directly
- the model can later be compiled into normal application code

That is a much better fit for a visual constructor than raw XAML text.

## Current Model Capabilities

The current `.arxui` format supports the essentials needed for a form designer:

- object creation by CLR type name
- nested controls and object graphs
- scalar property assignment
- collection properties such as `Children`
- attached properties such as `Grid.Row` and `Canvas.Left`
- resource references through `$resource`
- bindings through `$binding`
- asset loading through `$asset`

In practice this is enough for many layout- and form-oriented screens.

## What It Does Not Try To Be

This project is not currently a full XAML implementation. It does not aim to mirror the entire Avalonia XAML language surface.

Compared with XAML, the current model is intentionally narrower:

- no general markup extension system
- no full template/style/animation coverage
- no XAML namespace/prefix model
- no full parity with Avalonia XAML compiler behavior
- only the features explicitly implemented in the generator are supported

That tradeoff is acceptable for a designer-oriented DSL, as long as the model remains easy to edit and compile predictably.

## Design Direction

The right success criteria for this project are:

- can the designer represent the controls it needs
- can tools mutate the tree safely
- can the model round-trip cleanly
- can the generator produce deterministic code
- can preview and build-time generation stay aligned

If those goals are met, this approach can be more practical than XAML for interactive form-building workflows.

## Current Status

- The solution builds successfully.
- The sample app demonstrates the intended generator usage.
- The editor project demonstrates live tree/property manipulation.
- The test project covers the current `.arxui` schema.
