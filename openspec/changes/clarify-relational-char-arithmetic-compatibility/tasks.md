# Tasks

## 1. Character arithmetic compatibility

- [ ] 1.1 Add shared SQL Server/MySQL reproduction cases for parsed and handwritten `char` addition/subtraction with digit and non-digit characters; assert CLR code-point expected IDs and keep parser-success assertions separate from relational query execution.
- [ ] 1.2 Test an explicit `char`-to-integral EF Core value conversion on both providers; retain it only if generated and handwritten predicates return the expected same ID sets on both.
- [ ] 1.3 If no provider-neutral mapping preserves the CLR result, document the exact per-provider limitation and tested configuration without adding provider-specific SQL translation or altering parser support; run the focused provider tests and full integration project.
