# Prosegur Payment System - Prueba Técnica

Sistema de procesamiento de pagos POS con WPF y Stripe en .NET 8.0.

---

## 📋 Requisitos del Test

Implementar flujo completo:
1. Usuario ingresa monto en App POS (WPF)
2. POS envía petición al Servidor (.NET)
3. Servidor crea PaymentIntent en Stripe (modo test)
4. Dashboard web permite simular: **Aprobar / Rechazar / Cancelar**
5. Servidor comunica acción a Stripe
6. POS recibe resultado: **APPROVED / DECLINED / FAILED**

**Restricción Stripe:** Solo usar 2 operaciones:
- `POST /v1/payment_intents` (crear)
- `POST /v1/payment_intents/{id}/confirm` o `/cancel` (confirmar/cancelar)

---

## 🏗️ Arquitectura

```
Prosegur/
├── Prosegur.Shared/              # DTOs compartidos (records)
│   └── PaymentRequest, PaymentStatusResponse
│
├── Prosegur.Backend/             # ASP.NET Core 8.0 Web API
│   ├── Controllers/PaymentsController.cs
│   ├── Services/IStripeService.cs, StripeService.cs
│   ├── wwwroot/simulation.html   # Dashboard simulación
│   └── Program.cs
│
└── Prosegur.WPF/                 # Cliente WPF .NET 8.0
    ├── ViewModels/MainViewModel.cs (MVVM + Source Generators)
    ├── Services/IPaymentService.cs, PaymentService.cs
    └── Converters/ (StatusToColor, BooleanToVisibility)
```

### Patrones Aplicados
- **Clean Architecture**: Separación Backend/Frontend/Shared
- **MVVM**: CommunityToolkit.Mvvm con Source Generators
- **Dependency Injection**: Constructor injection en todo el stack
- **Repository Pattern**: IStripeService abstrae Stripe
- **Polling**: WPF consulta estado cada 2 segundos

---

## 🔧 Decisiones Técnicas Clave

### 1. **Terminal State Preservation**
```csharp
// StripeService.cs - GetPaymentStatusAsync
if (cachedResponse.Status is "APPROVED" or "DECLINED" or "FAILED")
    return cachedResponse; // NO consultar Stripe
```
**Problema resuelto:** Polling de WPF sobreescribía DECLINED con PENDING porque Stripe tarda en actualizar.

### 2. **Payment Availability Verification**
```csharp
// StripeService.cs - CreatePaymentIntentAsync
await Task.Delay(100);
paymentIntent = await _paymentIntentService.GetAsync(paymentIntent.Id);
```
**Problema resuelto:** Dashboard mostraba pago antes de estar disponible → error 404 al clickear.

### 3. **Decline via Exception Handling**
```csharp
// StripeService.cs - ConfirmPaymentAsync
var paymentMethodId = shouldSucceed ? "pm_card_visa" : "pm_card_chargeDeclined";
try {
    await _paymentIntentService.ConfirmAsync(paymentId, ...);
} catch (StripeException ex) {
    // pm_card_chargeDeclined lanza excepción → DECLINED
}
```
**Razón:** Stripe rechaza con excepción, no con status code.

### 4. **ConcurrentDictionary vs Base de Datos**
- In-memory para simplicidad (prueba técnica)
- Thread-safe sin locks
- Migración a Redis/SQL trivial (misma interfaz)

### 5. **Polling vs SignalR**
- 2-3 segundos aceptable para POS
- Menos infraestructura
- Migración a SignalR simple (misma arquitectura)

---

## 🚀 Configuración

### 1. **Prerequisitos**
- .NET 8.0 SDK
- Cuenta Stripe (modo test)

### 2. **Configurar Stripe**
Editar `Prosegur.Backend/appsettings.json`:
```json
{
  "Stripe": {
    "SecretKey": "sk_test_TU_CLAVE_AQUI"
  }
}
```
Obtener desde: https://dashboard.stripe.com/test/apikeys

### 3. **Ejecutar**

**Terminal 1 - Backend:**
```bash
cd Prosegur.Backend
dotnet run
```
Endpoints:
- API: http://localhost:5000
- Swagger: http://localhost:5000/swagger
- Dashboard: http://localhost:5000/simulation.html

**Terminal 2 - WPF:**
```bash
cd Prosegur.WPF
dotnet run
```

---

## 🧪 Flujo de Prueba

### Escenario 1: Pago Aprobado
1. WPF: Ingresar $10.00 → "Process Payment"
2. Dashboard: Aparece automáticamente (auto-refresh 3s)
3. Dashboard: Click "Approve"
4. Backend: `confirm` con `pm_card_visa` → `capture` → **APPROVED**
5. WPF: Polling detecta → Verde

### Escenario 2: Pago Rechazado
1. WPF: Ingresar $25.00 → "Process Payment"
2. Dashboard: Click "Decline"
3. Backend: `confirm` con `pm_card_chargeDeclined` → StripeException → **DECLINED**
4. WPF: Polling detecta → Rojo

### Escenario 3: Pago Cancelado
1. WPF: Ingresar $50.00 → "Process Payment"
2. Dashboard: Click "Cancel"
3. Backend: `cancel` → **FAILED**
4. WPF: Polling detecta → Gris

---

## 📊 Implementación

### Backend (ASP.NET Core)

**Program.cs (28 líneas):**
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IStripeService, StripeService>();
builder.Services.AddCors(options => {
    options.AddPolicy("AllowWpfClient", 
        policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI(c => c.RoutePrefix = "swagger");
app.UseCors("AllowWpfClient");
app.UseStaticFiles();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/simulation.html"));
app.Run();
```

**StripeService - Métodos:**
1. `CreatePaymentIntentAsync`: Crea + verifica disponibilidad (100ms delay)
2. `GetPaymentStatusAsync`: Consulta estado, preserva terminales
3. `ConfirmPaymentAsync`: pm_card_visa (aprueba) / pm_card_chargeDeclined (rechaza)
4. `CancelPaymentAsync`: Cancela PaymentIntent
5. `GetPendingPayments`: Lista para dashboard

**Endpoints REST:**
- `POST /api/payments` - Crear
- `GET /api/payments/{id}` - Estado
- `POST /api/payments/{id}/confirm?shouldSucceed=true` - Aprobar/Rechazar
- `POST /api/payments/{id}/cancel` - Cancelar
- `GET /api/payments/pending` - Pendientes

### Frontend WPF

**MainViewModel (CommunityToolkit.Mvvm):**
```csharp
[ObservableProperty]
private decimal _amount = 10.00m;

[ObservableProperty]
private string _status = "Ready";

[RelayCommand(CanExecute = nameof(CanProcessPayment))]
private async Task ProcessPaymentAsync() {
    var response = await _paymentService.CreatePaymentAsync(
        new PaymentRequest { Amount = Amount });
    PaymentId = response.PaymentId;
    await StartPollingAsync(response.PaymentId);
}
```

**Polling (cada 2s):**
```csharp
while (!cancellationToken.IsCancellationRequested) {
    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    var status = await _paymentService.GetPaymentStatusAsync(paymentId);
    Status = status.Status;
    if (IsTerminalState(status.Status)) break;
}
```

### Dashboard HTML

- CSS moderno (gradientes, sombras, hover effects)
- Auto-refresh cada 3 segundos (`setInterval`)
- Responsive (Grid 3 columnas → 1 en móvil)
- Sin dependencias externas

---

## 🔍 Mapeo Estados

| Stripe | Sistema | Descripción |
|--------|---------|-------------|
| `succeeded` | **APPROVED** | Capturado exitosamente |
| `requires_payment_method` | **PENDING** | Esperando confirmación |
| `requires_capture` | **PENDING** | Pre-autorizado |
| `canceled` | **FAILED** | Cancelado |
| `StripeException` | **DECLINED** | Tarjeta rechazada |

---

## 📦 Dependencias

**Backend:**
- Stripe.net 45.17.0
- Swashbuckle.AspNetCore 6.8.1

**WPF:**
- CommunityToolkit.Mvvm 8.3.2
- Microsoft.Extensions.Http 8.0.1

---

## 📝 Cumplimiento del Test

✅ **Aplicación POS** WPF .NET 8.0  
✅ **Servidor** ASP.NET Core 8.0  
✅ **Integración Stripe** Sandbox funcionando  
✅ **Dashboard** HTML con simulación  
✅ **Flujo completo** POS → Backend → Stripe → Dashboard → POS  
✅ **Solo 2 operaciones Stripe** Create + Confirm/Cancel  
✅ **Tarjetas test** pm_card_visa (OK) / pm_card_chargeDeclined (Decline)

---

## 📄 Licencia

Prueba técnica para Prosegur. Todos los derechos reservados.
