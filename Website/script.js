window.dataLayer = window.dataLayer || [];
function gtag() { dataLayer.push(arguments); }
gtag('js', new Date());
gtag('config', "G-MMXN05QKZ4");

const gaScript = document.createElement('script');
gaScript.async = true;
gaScript.src = `https://www.googletagmanager.com/gtag/js?id=G-MMXN05QKZ4`;
document.head.appendChild(gaScript);


// Scroll-triggered animations
document.addEventListener('DOMContentLoaded', () => {
    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('is-visible');
            }
        });
    }, {
        threshold: 0.1
    });

    const revealElements = document.querySelectorAll('.reveal-on-scroll');
    revealElements.forEach(el => {
        observer.observe(el);
    });

    // Hero card hover effect
    const heroCard = document.getElementById('hero-card');
    const mainNav = document.getElementById('main-nav');
    const heroFooter = document.getElementById('hero-footer');
    const heroImage = document.getElementById('hero-image');
    const visibilityToggle = document.getElementById('visibility-toggle');

    if (heroCard && mainNav && heroFooter && visibilityToggle) {
        visibilityToggle.addEventListener('mouseenter', () => {
            mainNav.classList.add('opacity-0');
            heroFooter.classList.add('!opacity-0');
            heroCard.classList.add('opacity-0');
            heroImage.classList.add('opacity-90');
            visibilityToggle.classList.add('opacity-0');
        });

        visibilityToggle.addEventListener('mouseleave', () => {
            mainNav.classList.remove('opacity-0');
            heroFooter.classList.remove('!opacity-0');
            heroCard.classList.remove('opacity-0');
            heroImage.classList.remove('opacity-90');
            visibilityToggle.classList.remove('opacity-0');
        });
    }

    // Navbar elements visibility on scroll
    const navLogo = document.getElementById('nav-logo');
    const navDownload = document.getElementById('nav-download');

    function updateNavVisibility() {
        if (navLogo && navDownload) {
            if (window.scrollY > window.innerHeight / 2) {
                navLogo.classList.remove('opacity-0', 'pointer-events-none');
                navDownload.classList.remove('opacity-0', 'pointer-events-none');
            } else {
                navLogo.classList.add('opacity-0', 'pointer-events-none');
                navDownload.classList.add('opacity-0', 'pointer-events-none');
            }
        }
    }

    window.addEventListener('scroll', updateNavVisibility);
    updateNavVisibility(); // Initial check

    // Winget button handler
    const wingetBtn = document.getElementById('winget-btn');
    const wingetContainer = document.getElementById('winget-container');
    const copyWingetBtn = document.getElementById('copy-winget');
    const wingetCode = document.getElementById('winget-code');

    if (wingetBtn && wingetContainer) {
        wingetBtn.addEventListener('click', () => {
            wingetContainer.classList.toggle('hidden');
        });
    }

    if (copyWingetBtn && wingetCode) {
        copyWingetBtn.addEventListener('click', () => {
            navigator.clipboard.writeText(wingetCode.innerText).then(() => {
                const originalIcon = copyWingetBtn.innerHTML;
                copyWingetBtn.innerHTML = `<svg class="w-4 h-4 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"></path></svg>`;
                setTimeout(() => {
                    copyWingetBtn.innerHTML = originalIcon;
                }, 1500);
            });
        });
    }
});
