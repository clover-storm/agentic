using ConcurrencyPattern.Api.DTOs;
using ConcurrencyPattern.Core.Entities;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Mediator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ConcurrencyPattern.Api.Controllers;

/// <summary>
/// 재고 관련 API 컨트롤러
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InventoriesController : ControllerBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InventoriesController> _logger;

    public InventoriesController(
        IInventoryService inventoryService,
        IUnitOfWork unitOfWork,
        ILogger<InventoriesController> logger)
    {
        _inventoryService = inventoryService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// 재고 목록 조회
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<InventoryResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var inventories = await _unitOfWork.Inventories.GetAllAsync(cancellationToken);
        return Ok(inventories.Select(i => new InventoryResponse
        {
            Id = i.Id,
            ProductCode = i.ProductCode,
            ProductName = i.ProductName,
            Quantity = i.Quantity,
            ReservedQuantity = i.ReservedQuantity,
            AvailableQuantity = i.AvailableQuantity
        }));
    }

    /// <summary>
    /// 재고 상세 조회
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InventoryResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var inventory = await _unitOfWork.Inventories.GetByIdAsync(id, cancellationToken);
        if (inventory == null)
            return NotFound();

        return Ok(new InventoryResponse
        {
            Id = inventory.Id,
            ProductCode = inventory.ProductCode,
            ProductName = inventory.ProductName,
            Quantity = inventory.Quantity,
            ReservedQuantity = inventory.ReservedQuantity,
            AvailableQuantity = inventory.AvailableQuantity
        });
    }

    /// <summary>
    /// 재고 생성 (테스트용)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<InventoryResponse>> Create(
        CreateInventoryRequest request,
        CancellationToken cancellationToken)
    {
        var inventory = new Inventory
        {
            Id = Guid.NewGuid(),
            ProductCode = request.ProductCode,
            ProductName = request.ProductName
        };

        if (request.InitialQuantity > 0)
        {
            inventory.AddStock(request.InitialQuantity);
        }

        await _unitOfWork.Inventories.AddAsync(inventory, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = inventory.Id }, new InventoryResponse
        {
            Id = inventory.Id,
            ProductCode = inventory.ProductCode,
            ProductName = inventory.ProductName,
            Quantity = inventory.Quantity,
            ReservedQuantity = inventory.ReservedQuantity,
            AvailableQuantity = inventory.AvailableQuantity
        });
    }

    /// <summary>
    /// 재고 추가 - 동일 재고에 대한 병렬 요청도 순차 처리됨
    /// </summary>
    [HttpPost("{id:guid}/add-stock")]
    public async Task<ActionResult<OperationResponse>> AddStock(
        Guid id,
        AddStockRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AddStock request for InventoryId={InventoryId}, Quantity={Quantity}",
            id, request.Quantity);

        var result = await _inventoryService.AddStockAsync(id, request.Quantity, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new OperationResponse
            {
                Success = false,
                Message = result.ErrorMessage
            });
        }

        return Ok(new OperationResponse
        {
            Success = true,
            Message = "재고가 추가되었습니다.",
            Data = new
            {
                CurrentQuantity = result.CurrentQuantity,
                AvailableQuantity = result.AvailableQuantity
            }
        });
    }

    /// <summary>
    /// 재고 예약 - 동시 주문 시에도 무결성 보장
    /// </summary>
    [HttpPost("{id:guid}/reserve")]
    public async Task<ActionResult<OperationResponse>> Reserve(
        Guid id,
        ReserveStockRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "API: Reserve request for InventoryId={InventoryId}, Quantity={Quantity}, OrderId={OrderId}",
            id, request.Quantity, request.OrderId);

        var result = await _inventoryService.ReserveStockAsync(
            id, request.Quantity, request.OrderId, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new OperationResponse
            {
                Success = false,
                Message = result.ErrorMessage
            });
        }

        return Ok(new OperationResponse
        {
            Success = true,
            Message = "재고가 예약되었습니다.",
            Data = new
            {
                CurrentQuantity = result.CurrentQuantity,
                AvailableQuantity = result.AvailableQuantity
            }
        });
    }

    /// <summary>
    /// 예약 확정
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<OperationResponse>> Confirm(
        Guid id,
        ReserveStockRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "API: Confirm reservation for InventoryId={InventoryId}, Quantity={Quantity}, OrderId={OrderId}",
            id, request.Quantity, request.OrderId);

        var result = await _inventoryService.ConfirmReservationAsync(
            id, request.Quantity, request.OrderId, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new OperationResponse
            {
                Success = false,
                Message = result.ErrorMessage
            });
        }

        return Ok(new OperationResponse
        {
            Success = true,
            Message = "예약이 확정되었습니다.",
            Data = new
            {
                CurrentQuantity = result.CurrentQuantity,
                AvailableQuantity = result.AvailableQuantity
            }
        });
    }

    /// <summary>
    /// 예약 취소
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OperationResponse>> Cancel(
        Guid id,
        ReserveStockRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "API: Cancel reservation for InventoryId={InventoryId}, Quantity={Quantity}, OrderId={OrderId}",
            id, request.Quantity, request.OrderId);

        var result = await _inventoryService.CancelReservationAsync(
            id, request.Quantity, request.OrderId, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new OperationResponse
            {
                Success = false,
                Message = result.ErrorMessage
            });
        }

        return Ok(new OperationResponse
        {
            Success = true,
            Message = "예약이 취소되었습니다.",
            Data = new
            {
                CurrentQuantity = result.CurrentQuantity,
                AvailableQuantity = result.AvailableQuantity
            }
        });
    }
}
