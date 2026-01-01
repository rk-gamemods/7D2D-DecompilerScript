using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CallGraphExtractor;

/// <summary>
/// Extracts event subscriptions (+= / -=), event declarations, and event fires
/// from both game code and mod code. This enables behavioral flow analysis
/// that traces cause → effect through event-driven patterns.
/// </summary>
public class EventFlowExtractor
{
    private readonly SqliteWriter _db;
    private readonly bool _verbose;
    
    // Statistics
    private int _declarationCount;
    private int _subscriptionCount;
    private int _fireCount;
    
    // Known event patterns that are commonly subscribed to
    private static readonly HashSet<string> KnownEventPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "DragAndDropItemChanged",
        "OnBackpackItemsChangedInternal",
        "OnToolbeltItemsChangedInternal",
        "OnInventoryChanged",
        "ItemsChanged",
        "SlotChanged",
        "OnOpen",
        "OnClose",
        "OnValueChanged",
        "OnClick",
        "OnPress"
    };
    
    public EventFlowExtractor(SqliteWriter db, bool verbose = false)
    {
        _db = db;
        _verbose = verbose;
    }
    
    public int DeclarationCount => _declarationCount;
    public int SubscriptionCount => _subscriptionCount;
    public int FireCount => _fireCount;
    
    /// <summary>
    /// Extract events from a parsed syntax tree with semantic model.
    /// </summary>
    public void ExtractFromTree(SyntaxTree tree, SemanticModel? semanticModel, 
                                 string filePath, bool isMod = false, long? modId = null)
    {
        var root = tree.GetRoot();
        
        // Extract event declarations (event keyword)
        ExtractEventDeclarations(root, semanticModel, filePath);
        
        // Extract event subscriptions (+= and -=)
        ExtractEventSubscriptions(root, semanticModel, filePath, isMod, modId);
        
        // Extract event fires (Invoke, DynamicInvoke, direct delegate calls)
        ExtractEventFires(root, semanticModel, filePath, isMod, modId);
    }
    
    /// <summary>
    /// Extract event field/property declarations.
    /// </summary>
    private void ExtractEventDeclarations(SyntaxNode root, SemanticModel? semanticModel, string filePath)
    {
        // Find event field declarations: public event Action<T> EventName;
        foreach (var eventDecl in root.DescendantNodes().OfType<EventFieldDeclarationSyntax>())
        {
            var containingType = GetContainingTypeName(eventDecl);
            if (containingType == null) continue;
            
            var delegateType = eventDecl.Declaration.Type.ToString();
            var isPublic = eventDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
            
            foreach (var variable in eventDecl.Declaration.Variables)
            {
                var eventName = variable.Identifier.Text;
                var lineSpan = variable.GetLocation().GetLineSpan();
                var lineNumber = lineSpan.StartLinePosition.Line + 1;
                
                _db.InsertEventDeclaration(containingType, eventName, delegateType, isPublic, filePath, lineNumber);
                _declarationCount++;
                
                if (_verbose)
                    Console.WriteLine($"    Event declaration: {containingType}.{eventName}");
            }
        }
        
        // Find event property declarations: public event Action<T> EventName { add; remove; }
        foreach (var eventDecl in root.DescendantNodes().OfType<EventDeclarationSyntax>())
        {
            var containingType = GetContainingTypeName(eventDecl);
            if (containingType == null) continue;
            
            var eventName = eventDecl.Identifier.Text;
            var delegateType = eventDecl.Type.ToString();
            var isPublic = eventDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
            var lineSpan = eventDecl.GetLocation().GetLineSpan();
            var lineNumber = lineSpan.StartLinePosition.Line + 1;
            
            _db.InsertEventDeclaration(containingType, eventName, delegateType, isPublic, filePath, lineNumber);
            _declarationCount++;
            
            if (_verbose)
                Console.WriteLine($"    Event declaration: {containingType}.{eventName}");
        }
    }
    
    /// <summary>
    /// Extract event subscriptions (+= and -= operators).
    /// Patterns:
    ///   - player.DragAndDropItemChanged += HandleItemChange;
    ///   - Backpack.OnBackpackItemsChangedInternal -= ItemsChangedInternal;
    ///   - eventField.GetValue(obj) as Delegate += ...
    /// </summary>
    private void ExtractEventSubscriptions(SyntaxNode root, SemanticModel? semanticModel,
                                            string filePath, bool isMod, long? modId)
    {
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var opKind = assignment.OperatorToken.Kind();
            if (opKind != SyntaxKind.PlusEqualsToken && opKind != SyntaxKind.MinusEqualsToken)
                continue;
            
            var subscriptionType = opKind == SyntaxKind.PlusEqualsToken ? "add" : "remove";
            
            // Get the left side - should be member access (obj.EventName) or identifier
            string? eventOwnerType = null;
            string? eventName = null;
            
            if (assignment.Left is MemberAccessExpressionSyntax memberAccess)
            {
                eventName = memberAccess.Name.Identifier.Text;
                
                // Try to get the type from semantic model
                if (semanticModel != null)
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression);
                    var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
                    eventOwnerType = typeInfo.Type?.ToDisplayString() ?? 
                                     symbolInfo.Symbol?.ContainingType?.ToDisplayString();
                }
                
                // Fallback: use the expression text
                if (eventOwnerType == null)
                {
                    eventOwnerType = GuessTypeFromExpression(memberAccess.Expression);
                }
            }
            else if (assignment.Left is IdentifierNameSyntax identifier)
            {
                eventName = identifier.Identifier.Text;
                eventOwnerType = GetContainingTypeName(assignment);
            }
            
            if (eventName == null) continue;
            
            // Check if this looks like an event (heuristic)
            if (!LooksLikeEvent(eventName, semanticModel, assignment.Left))
                continue;
            
            // Get the handler method from the right side
            var handlerMethod = ExtractHandlerMethod(assignment.Right);
            if (handlerMethod == null) continue;
            
            // Get containing type and method
            var subscriberType = GetContainingTypeName(assignment);
            var subscriberMethodName = GetContainingMethodName(assignment);
            
            var lineSpan = assignment.GetLocation().GetLineSpan();
            var lineNumber = lineSpan.StartLinePosition.Line + 1;
            
            _db.InsertEventSubscription(
                subscriberType ?? "Unknown",
                eventOwnerType ?? "Unknown",
                eventName,
                handlerMethod,
                subscriberType,
                subscriptionType,
                isMod,
                modId,
                filePath,
                lineNumber
            );
            _subscriptionCount++;
            
            if (_verbose)
                Console.WriteLine($"    Event subscription: {subscriberType}.{subscriberMethodName} " +
                                  $"{subscriptionType}s {eventOwnerType}.{eventName} → {handlerMethod}");
        }
    }
    
    /// <summary>
    /// Extract event fires (invocations).
    /// Patterns:
    ///   - EventName?.Invoke(args)
    ///   - EventName.Invoke(args)
    ///   - EventName(args)  -- direct delegate call
    ///   - DynamicInvoke()  -- reflection-based
    ///   - eventDelegate?.DynamicInvoke()
    /// </summary>
    private void ExtractEventFires(SyntaxNode root, SemanticModel? semanticModel,
                                    string filePath, bool isMod, long? modId)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? eventOwnerType = null;
            string? eventName = null;
            string fireMethod = "direct";
            bool isConditional = false;
            
            // Pattern: obj?.EventName?.Invoke() or obj.EventName.Invoke()
            if (invocation.Expression is MemberAccessExpressionSyntax methodAccess)
            {
                var methodName = methodAccess.Name.Identifier.Text;
                
                if (methodName == "Invoke" || methodName == "DynamicInvoke")
                {
                    fireMethod = methodName;
                    
                    // Check for null-conditional
                    isConditional = methodAccess.Expression is ConditionalAccessExpressionSyntax ||
                                    invocation.Parent is ConditionalAccessExpressionSyntax;
                    
                    // Get the event from the expression before .Invoke
                    if (methodAccess.Expression is MemberAccessExpressionSyntax eventAccess)
                    {
                        eventName = eventAccess.Name.Identifier.Text;
                        eventOwnerType = GuessTypeFromExpression(eventAccess.Expression);
                    }
                    else if (methodAccess.Expression is IdentifierNameSyntax eventId)
                    {
                        eventName = eventId.Identifier.Text;
                        eventOwnerType = GetContainingTypeName(invocation);
                    }
                    else if (methodAccess.Expression is ConditionalAccessExpressionSyntax conditional)
                    {
                        // Handle obj?.EventName?.Invoke()
                        if (conditional.WhenNotNull is MemberBindingExpressionSyntax binding)
                        {
                            eventName = binding.Name.Identifier.Text;
                            eventOwnerType = GuessTypeFromExpression(conditional.Expression);
                        }
                    }
                }
            }
            // Pattern: eventDelegate(args) - direct delegate invocation
            else if (invocation.Expression is IdentifierNameSyntax identifier)
            {
                var name = identifier.Identifier.Text;
                if (LooksLikeEventName(name))
                {
                    eventName = name;
                    eventOwnerType = GetContainingTypeName(invocation);
                    fireMethod = "direct";
                }
            }
            
            if (eventName == null || !LooksLikeEventName(eventName))
                continue;
            
            var firingType = GetContainingTypeName(invocation);
            var lineSpan = invocation.GetLocation().GetLineSpan();
            var lineNumber = lineSpan.StartLinePosition.Line + 1;
            
            _db.InsertEventFire(
                firingType ?? "Unknown",
                eventOwnerType ?? firingType ?? "Unknown",
                eventName,
                fireMethod,
                isConditional,
                isMod,
                modId,
                filePath,
                lineNumber
            );
            _fireCount++;
            
            if (_verbose)
                Console.WriteLine($"    Event fire: {firingType} fires {eventOwnerType}.{eventName} via {fireMethod}");
        }
    }
    
    /// <summary>
    /// Heuristic: Does this name look like an event?
    /// </summary>
    private bool LooksLikeEvent(string name, SemanticModel? semanticModel, ExpressionSyntax expression)
    {
        // Known event patterns
        if (KnownEventPatterns.Contains(name))
            return true;
        
        // Common event naming patterns
        if (name.StartsWith("On") || name.EndsWith("Changed") || name.EndsWith("Event") ||
            name.EndsWith("Fired") || name.EndsWith("Triggered") || name.EndsWith("Handler") ||
            name.Contains("ItemsChanged") || name.Contains("Click") || name.Contains("Press"))
            return true;
        
        // Try semantic model
        if (semanticModel != null)
        {
            var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
            if (symbol is IEventSymbol)
                return true;
            if (symbol is IFieldSymbol field && 
                (field.Type.Name.Contains("Action") || field.Type.Name.Contains("EventHandler")))
                return true;
        }
        
        return false;;
    }
    
    /// <summary>
    /// Simple check if name looks like an event name.
    /// </summary>
    private bool LooksLikeEventName(string name)
    {
        return KnownEventPatterns.Contains(name) ||
               name.StartsWith("On") || 
               name.EndsWith("Changed") || 
               name.EndsWith("Event") ||
               name.Contains("ItemsChanged");
    }
    
    /// <summary>
    /// Extract the handler method name from the right side of a subscription.
    /// </summary>
    private string? ExtractHandlerMethod(ExpressionSyntax expression)
    {
        // Simple identifier: += HandleChange
        if (expression is IdentifierNameSyntax id)
            return id.Identifier.Text;
        
        // Member access: += this.HandleChange or += SomeClass.HandleChange
        if (expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.Text;
        
        // Lambda: += (args) => { ... }
        if (expression is SimpleLambdaExpressionSyntax || expression is ParenthesizedLambdaExpressionSyntax)
            return "<lambda>";
        
        // Object creation for delegate: += new EventHandler(Method)
        if (expression is ObjectCreationExpressionSyntax creation)
        {
            if (creation.ArgumentList?.Arguments.Count > 0)
            {
                var arg = creation.ArgumentList.Arguments[0].Expression;
                if (arg is IdentifierNameSyntax argId)
                    return argId.Identifier.Text;
                if (arg is MemberAccessExpressionSyntax argMember)
                    return argMember.Name.Identifier.Text;
            }
        }
        
        return expression.ToString();
    }
    
    /// <summary>
    /// Try to guess the type from an expression (without semantic model).
    /// </summary>
    private string? GuessTypeFromExpression(ExpressionSyntax expression)
    {
        // Simple identifier - assume it's a local variable, try to find type from context
        if (expression is IdentifierNameSyntax id)
        {
            var name = id.Identifier.Text;
            // Common patterns
            if (name == "player" || name == "entityPlayer" || name == "_player")
                return "EntityPlayerLocal";
            if (name == "backpack" || name == "Backpack" || name == "_backpack")
                return "Bag";
            if (name == "toolbelt" || name == "Toolbelt" || name == "_toolbelt")
                return "Inventory";
            if (name.Contains("inventory") || name.Contains("Inventory"))
                return "XUiM_PlayerInventory";
            return name; // Return the variable name as placeholder
        }
        
        // Member access: playerInventory.Backpack
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.Text;
            if (memberName == "Backpack") return "Bag";
            if (memberName == "Toolbelt") return "Inventory";
            if (memberName == "Player") return "EntityPlayerLocal";
            return memberName;
        }
        
        // This keyword
        if (expression is ThisExpressionSyntax)
            return GetContainingTypeName(expression);
        
        return null;
    }
    
    /// <summary>
    /// Get the fully qualified name of the containing type.
    /// </summary>
    private string? GetContainingTypeName(SyntaxNode node)
    {
        var typeDecl = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl == null) return null;
        
        var namespaceName = GetNamespace(typeDecl);
        var typeName = typeDecl.Identifier.Text;
        
        return string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
    }
    
    /// <summary>
    /// Get the name of the containing method.
    /// </summary>
    private string? GetContainingMethodName(SyntaxNode node)
    {
        var methodDecl = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return methodDecl?.Identifier.Text;
    }
    
    /// <summary>
    /// Get the namespace for a type declaration.
    /// </summary>
    private string? GetNamespace(TypeDeclarationSyntax typeDecl)
    {
        // Check for file-scoped namespace
        var fileScopedNs = typeDecl.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        if (fileScopedNs != null)
            return fileScopedNs.Name.ToString();
        
        // Check for block-scoped namespace
        var blockNs = typeDecl.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
        if (blockNs != null)
            return blockNs.Name.ToString();
        
        return null;
    }
    
    /// <summary>
    /// Print extraction summary.
    /// </summary>
    public void PrintSummary()
    {
        Console.WriteLine($"  Event extraction complete:");
        Console.WriteLine($"    Declarations: {_declarationCount}");
        Console.WriteLine($"    Subscriptions: {_subscriptionCount}");
        Console.WriteLine($"    Fires: {_fireCount}");
    }
}
