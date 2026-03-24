// ============================================================
// ScrapSAE Landing Page - Main JavaScript
// Sin frameworks ni dependencias de compilación.
// ============================================================

(function () {
  'use strict';

  // ============================================================
  // Configuración
  // ============================================================
  var API_BASE_URL = 'https://api.scrapsae.com';

  // ============================================================
  // Navbar scroll effect
  // ============================================================
  var navbar = document.getElementById('navbar');
  var lastScroll = 0;

  window.addEventListener('scroll', function () {
    var currentScroll = window.pageYOffset;
    if (currentScroll > 50) {
      navbar.classList.add('scrolled');
    } else {
      navbar.classList.remove('scrolled');
    }
    lastScroll = currentScroll;
  });

  // ============================================================
  // Mobile menu toggle
  // ============================================================
  var navbarToggle = document.getElementById('navbar-toggle');
  if (navbarToggle) {
    navbarToggle.addEventListener('click', function () {
      navbar.classList.toggle('mobile-open');
    });
  }

  // Close mobile menu on link click
  var navLinks = document.querySelectorAll('.navbar-nav a');
  navLinks.forEach(function (link) {
    link.addEventListener('click', function () {
      navbar.classList.remove('mobile-open');
    });
  });

  // ============================================================
  // FAQ Accordion
  // ============================================================
  var faqItems = document.querySelectorAll('.faq-item');
  faqItems.forEach(function (item) {
    var question = item.querySelector('.faq-question');
    var answer = item.querySelector('.faq-answer');

    question.addEventListener('click', function () {
      var isOpen = item.classList.contains('open');

      // Close all
      faqItems.forEach(function (other) {
        other.classList.remove('open');
        var otherAnswer = other.querySelector('.faq-answer');
        if (otherAnswer) otherAnswer.style.maxHeight = '0';
      });

      // Toggle current
      if (!isOpen) {
        item.classList.add('open');
        answer.style.maxHeight = answer.scrollHeight + 'px';
      }
    });
  });

  // ============================================================
  // Smooth scroll for anchor links
  // ============================================================
  document.querySelectorAll('a[href^="#"]').forEach(function (anchor) {
    anchor.addEventListener('click', function (e) {
      var target = document.querySelector(this.getAttribute('href'));
      if (target) {
        e.preventDefault();
        var offset = 80; // navbar height
        var top = target.getBoundingClientRect().top + window.pageYOffset - offset;
        window.scrollTo({ top: top, behavior: 'smooth' });
      }
    });
  });

  // ============================================================
  // Intersection Observer for animations
  // ============================================================
  var animateElements = document.querySelectorAll(
    '.feature-card, .step, .pricing-card, .faq-item'
  );

  if ('IntersectionObserver' in window) {
    var observer = new IntersectionObserver(
      function (entries) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) {
            entry.target.style.opacity = '1';
            entry.target.style.transform = 'translateY(0)';
            observer.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.1, rootMargin: '0px 0px -50px 0px' }
    );

    animateElements.forEach(function (el) {
      el.style.opacity = '0';
      el.style.transform = 'translateY(20px)';
      el.style.transition = 'opacity 0.5s ease, transform 0.5s ease';
      observer.observe(el);
    });
  }

  // ============================================================
  // Stripe Checkout Integration
  // ============================================================
  window.handleCheckout = function (planType) {
    // En producción, esto llamaría al API para crear una sesión de Stripe
    // Por ahora, redirige a la página de contacto para Enterprise
    if (planType === 'enterprise') {
      window.location.href = 'mailto:ventas@scrapsae.com?subject=ScrapSAE Enterprise';
      return;
    }

    // Crear sesión de checkout
    fetch(API_BASE_URL + '/api/stripe/create-checkout', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        planType: planType,
        email: '', // Se solicitará en Stripe Checkout
      }),
    })
      .then(function (response) {
        return response.json();
      })
      .then(function (data) {
        if (data.checkoutUrl) {
          window.location.href = data.checkoutUrl;
        } else {
          alert(
            'Error al iniciar el proceso de pago. Por favor, intenta de nuevo.'
          );
        }
      })
      .catch(function (error) {
        console.error('Checkout error:', error);
        alert(
          'No se pudo conectar con el servidor de pagos. Verifica tu conexión e intenta de nuevo.'
        );
      });
  };

  // ============================================================
  // Chrome / Firefox Install Buttons
  // ============================================================
  var btnChrome = document.getElementById('btn-chrome-install');
  var btnFirefox = document.getElementById('btn-firefox-install');

  if (btnChrome) {
    btnChrome.addEventListener('click', function (e) {
      e.preventDefault();
      // Reemplazar con la URL real de Chrome Web Store
      window.open(
        'https://chrome.google.com/webstore/detail/scrapsae/EXTENSION_ID',
        '_blank'
      );
    });
  }

  if (btnFirefox) {
    btnFirefox.addEventListener('click', function (e) {
      e.preventDefault();
      // Reemplazar con la URL real de Firefox Add-ons
      window.open(
        'https://addons.mozilla.org/es/firefox/addon/scrapsae/',
        '_blank'
      );
    });
  }

  // ============================================================
  // Typing animation for hero mockup
  // ============================================================
  var mockupLines = document.querySelectorAll('.hero-mockup-body .line');
  if (mockupLines.length > 0) {
    mockupLines.forEach(function (line, index) {
      line.style.opacity = '0';
      line.style.transform = 'translateX(-10px)';
      line.style.transition = 'opacity 0.4s ease, transform 0.4s ease';

      setTimeout(function () {
        line.style.opacity = '1';
        line.style.transform = 'translateX(0)';
      }, 300 + index * 200);
    });
  }

  // ============================================================
  // Counter animation for hero stats
  // ============================================================
  function animateCounter(element, target, suffix) {
    var current = 0;
    var increment = target / 40;
    var timer = setInterval(function () {
      current += increment;
      if (current >= target) {
        current = target;
        clearInterval(timer);
      }
      element.textContent = Math.floor(current).toLocaleString() + (suffix || '');
    }, 30);
  }

  var statsObserver = new IntersectionObserver(
    function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          var stats = entry.target.querySelectorAll('.hero-stat .number');
          if (stats[0]) animateCounter(stats[0], 50, '+');
          if (stats[1]) animateCounter(stats[1], 10000, '+');
          if (stats[2]) {
            var el = stats[2];
            var current = 0;
            var timer = setInterval(function () {
              current += 2.5;
              if (current >= 99.5) {
                current = 99.5;
                clearInterval(timer);
              }
              el.textContent = current.toFixed(1) + '%';
            }, 30);
          }
          statsObserver.unobserve(entry.target);
        }
      });
    },
    { threshold: 0.5 }
  );

  var heroStats = document.querySelector('.hero-stats');
  if (heroStats) {
    statsObserver.observe(heroStats);
  }
})();
