﻿using AutoMapper;
using Azure.Core;
using Contracts;
using EmailService;
using Entities.Identity;
using Entities.Models;
using MailKit.Search;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Asn1.Ocsp;
using Repository;
using Shared.DTO.Address;
using Shared.DTO.Cart;
using Shared.DTO.Contact;
using Shared.DTO.Order;
using Shared.DTO.Response;

namespace Ecommerce_Wolmart.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        private readonly ILoggerManager _logger;
        private readonly IRepositoryManager _repository;
        private readonly IMapper _mapper;
        private readonly IEmailSender _emailSender;

        public OrderController(ILoggerManager logger, IRepositoryManager repository, IMapper mapper, IEmailSender emailSender)
        {
            _logger = logger;
            _repository = repository;
            _mapper = mapper;
            _emailSender = emailSender;
        }

        [HttpPost]
        [Route("CreateOrder")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto createOrderDto)
        {
            try
            {
                if (createOrderDto == null)
                {
                    return BadRequest(new ApiResponse<Object>
                    {
                        Success = false,
                        Message = "Invalid request data..",
                        Data = null
                    });
                }

                if (!ModelState.IsValid)
                {
                    _logger.LogError("Invalid CreateOrder object sent from client.");
                    return NotFound(new ApiResponse<Object>
                    {
                        Success = false,
                        Message = $"Invalid model object",
                        Data = null
                    });
                }

                // Kiểm tra xem UserId đã có đơn hàng chưa
                //var existingOrder = await _repository.Order.GetOrderByUserIdAsync(createOrderDto.UserId.ToString());
                //if (existingOrder != null)
                //{
                //    return Conflict(new ApiResponse<Object>
                //    {
                //        Success = false,
                //        Message = $"Đơn hàng đã tồn tại cho người dùng với ID {createOrderDto.UserId}.",
                //        Data = null
                //    });
                //}

                // Lấy thông tin địa chỉ
                var address = await _repository.Address.GetAddressByIdAsync(createOrderDto.AddressId, trackChanges: false);
                if (address == null)
                {
                    return NotFound(new ApiResponse<Object>
                    {
                        Success = false,
                        Message = $"Không tìm thấy địa chỉ của {createOrderDto.AddressId}!",
                        Data = null
                    });
                }

                // Lấy phí vận chuyển dựa trên mã tỉnh
                var shippingCost = await _repository.ShippingCost.GetShippingCostByProvinceCodeAsync(address.ProvinceCode);
                if (shippingCost == null)
                {
                    return NotFound(new ApiResponse<Object>
                    {
                        Success = false,
                        Message = $"Không tìm phí ship của !",
                        Data = null
                    });
                }

                // Tạo mã đơn hàng dựa trên thời gian và chuỗi ngẫu nhiên
                var orderCode = GenerateOrderCode();

                // Lấy danh sách sản phẩm trong giỏ hàng
                var cartItems = await _repository.Cart.GetCartItemsByUserIdAsync(createOrderDto.UserId, trackChanges: false);
                if (cartItems == null || !cartItems.Any())
                {
                    return NotFound(new ApiResponse<Object>
                    {
                        Success = false,
                        Message = $"Không tìm thấy giỏ hàng của người dùng này {createOrderDto.UserId}!",
                        Data = null
                    });
                }

                var totalDiscount = cartItems.Sum(item => item.Discount * item.Quantity);
                var totalPrice = cartItems.Sum(item => item.Price * item.Quantity);

                    
                // Tạo đợn hàng mới
                var order = _mapper.Map<Order>(createOrderDto);

                order.Id = Guid.NewGuid();
                order.AddressId = address.Id;
                order.ShippingCostId = shippingCost.Id;
                order.OrderCode = orderCode;
                order.Discount = totalDiscount;
                order.UserName = address.UserName;
                order.PhoneNumber = address.PhoneNumber;
                order.Price = totalPrice;
                order.ShippingCost = shippingCost.Cost;
                order.OrderStatus = false;
                order.TotalAmount = totalPrice - totalDiscount + shippingCost.Cost;
                order.OrderDate = DateTime.UtcNow;
                

                var orderItems = cartItems.Select(item => new OrderItemDto
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    ProductName = item.Name,
                    CategoryName = item.CategoryName,
                    Quantity = item.Quantity,
                    Price = item.Price,
                    Discount = item.Discount,
                    ImageFilePath = item.ImageFilePath
                }).ToList();

                //Map giỏ hàng vào orderItem
                var orderItemEntities = _mapper.Map<IEnumerable<OrderItem>>(orderItems);

                order.OrderItems = orderItemEntities.ToList();


                await _repository.Order.CreateOrderAsync(order);

                //await _repository.Order.AddOrderItemsAsync(orderItems);

                await _repository.Order.SaveAsync();

                var orderDto = _mapper.Map<OrderDto>(order);

                var emailContent = $@"
                            <h3>Đơn hàng {order.OrderCode} của bạn đã được đặt thành công!</h3>
                            <p>Xin chào {address.UserName},</p>
                            <p>Cảm ơn bạn đã đặt hàng tại cửa hàng của chúng tôi. Dưới đây là thông tin chi tiết về đơn hàng của bạn:</p>
                            <ul>
                                <li><strong>Mã đơn hàng:</strong> {order.OrderCode}</li>
                                <li><strong>Ngày đặt hàng:</strong> {order.OrderDate:dd/MM/yyyy}</li>
                                <li><strong>Tổng tiền:</strong> {order.TotalAmount:C}</li>
                                <li><strong>Địa chỉ giao hàng:</strong> {address.UserName}</li>
                            </ul>
                            <h4>Sản phẩm trong đơn hàng:</h4>
                            <ul>
                                {string.Join("", orderItems.Select(item => $"<li>{item.ProductName} (x{item.Quantity}) - Giá: {item.Price:C}</li>"))}
                            </ul>
                            <p>Phí vận chuyển: {shippingCost.Cost:C}</p>
                            <p>Chúng tôi sẽ liên hệ với bạn khi đơn hàng được giao.</p>
                            <p>Cảm ơn bạn!</p>";

                var message = new Message(new string[] { order.Email }, "Xác nhận đơn hàng", emailContent);

                await _emailSender.SendEmailAsync(message);


                await _repository.Cart.DeleteCartItemsByUserIdAsync(order.UserId);
                await _repository.Cart.SaveAsync();

                return Ok(new ApiResponse<OrderDto>
                {
                    Success = true,
                    Message = "Order created successfully.",
                    Data = orderDto
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Something went wrong inside BannerProduct action: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        [Route("GetOrderById/{orderId}")]
        public async Task<IActionResult> GetOrderById(Guid orderId)
        {
            var order = await _repository.Order.GetOrderByIdAsync(orderId, trackChanges: false);

            if (order == null)
            {
                return NotFound(new ApiResponse<Object>
                {
                    Success = false,
                    Message = "Order not found.",
                    Data = null
                });
            }

            var orderDto = _mapper.Map<OrderDto>(order);
            orderDto.OrderCode = order.OrderCode;

            return Ok(new ApiResponse<OrderDto>
            {
                Success = true,
                Message = "Order details retrieved successfully.",
                Data = orderDto
            });
        }

        [HttpPut]
        [Route("UpdateOrderStatus/{Id}")]
        public async Task<IActionResult> UpdateOrderStatus(Guid Id, [FromQuery] UpdateOrderDto updateOrderDto)
        {
            try
            {
                var order = await _repository.Order.GetOrderByIdAsync(Id, trackChanges: false);
                if (order == null)
                {
                    return NotFound(new ApiResponse<Object>
                    {
                        Success = false,
                        Message = $"Order with id {Id} not found.",
                        Data = null
                    });
                }

                // Cập nhật trạng thái đơn hàng
                order.OrderStatus = updateOrderDto.OrderStatus; // Cập nhật thành đã duyệt (true tương đương với 1)

                _repository.Order.UpdateOrderAsync(order);
                await _repository.Order.SaveAsync();

               return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Something went wrong while updating order status: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        [Route("GetPendingOrders")]
        public async Task<IActionResult> GetPendingOrders()
        {
            try
            {
                var orders = await _repository.Order.GetPendingOrdersAsync(trackChanges: false);
                if (orders == null || !orders.Any())
                {
                    return NotFound(new ApiResponse<Object>
                    {
                        Success = false,
                        Message = "No pending orders found.",
                        Data = null
                    });
                }

                var ordersDto = _mapper.Map<IEnumerable<OrderDto>>(orders);
                return Ok(new ApiResponse<IEnumerable<OrderDto>>
                {
                    Success = true,
                    Message = "Pending orders retrieved successfully.",
                    Data = ordersDto
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Something went wrong while getting pending orders: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete]
        [Route("DeleteOrder/{Id}")]
        public async Task<IActionResult> DeleteOrder(Guid Id)
        {
            try
            {
                var order = await _repository.Order.GetOrderByIdAsync(Id, trackChanges: false);
                if (order == null)
                {
                    return NotFound(new ApiResponse<Object>
                    {
                        Success = false,
                        Message = $"Không tìm thấy đơn hàng với ID {Id}!",
                        Data = null
                    });
                }

                // Kiểm tra xem có sản phẩm nào trong giỏ hàng của người dùng hay không
                //var cartItems = await _repository.Cart.GetCartItemsByUserIdAsync(order.UserId, trackChanges: false);
                //if (cartItems != null && cartItems.Any())
                //{
                //    return BadRequest(new ApiResponse<Object>
                //    {
                //        Success = false,
                //        Message = "Không thể xóa đơn hàng vì giỏ hàng của người dùng vẫn còn sản phẩm.",
                //        Data = null
                //    });
                //}

                _repository.Order.DeleteOrderAsync(order);
                _repository.SaveAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Something went wrong inside DeleteBrand action: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }


        // Phương thức tạo mã đơn hàng
        private string GenerateOrderCode()
        {
            var random = new Random();
            var randomPart = random.Next(1000, 9999).ToString(); // Chuỗi ngẫu nhiên gồm 4 số
            var timePart = DateTime.Now.ToString("yyyyMMddHHmmss"); // Chuỗi thời gian hiện tại

            return $"OD{timePart}{randomPart}";
        }
    }
}
