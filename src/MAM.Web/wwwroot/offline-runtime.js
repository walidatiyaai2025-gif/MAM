(() => {
  'use strict';

  const resolveTarget = element => {
    const raw = element?.getAttribute('data-bs-target') || element?.getAttribute('href');
    if (!raw || !raw.startsWith('#')) return null;
    try { return document.querySelector(raw); } catch { return null; }
  };

  const setExpanded = (trigger, expanded) => {
    if (!trigger) return;
    trigger.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    trigger.classList.toggle('collapsed', !expanded);
  };

  class Collapse {
    constructor(element) { this._element = typeof element === 'string' ? document.querySelector(element) : element; }
    show() { if (this._element) this._element.classList.add('show'); }
    hide() { if (this._element) this._element.classList.remove('show'); }
    toggle() { if (this._element) this._element.classList.toggle('show'); }
    static getOrCreateInstance(element) { return new Collapse(element); }
  }

  class Dropdown {
    constructor(element) { this._element = element; }
    _menu() { return this._element?.parentElement?.querySelector('.dropdown-menu') || null; }
    show() { this._menu()?.classList.add('show'); this._element?.setAttribute('aria-expanded', 'true'); }
    hide() { this._menu()?.classList.remove('show'); this._element?.setAttribute('aria-expanded', 'false'); }
    toggle() { const menu = this._menu(); if (!menu) return; menu.classList.contains('show') ? this.hide() : this.show(); }
    static getOrCreateInstance(element) { return new Dropdown(element); }
  }

  class Modal {
    constructor(element) { this._element = typeof element === 'string' ? document.querySelector(element) : element; }
    show() {
      if (!this._element) return;
      this._element.classList.add('show');
      this._element.style.display = 'block';
      this._element.removeAttribute('aria-hidden');
      this._element.setAttribute('aria-modal', 'true');
      document.body.classList.add('modal-open');
      this._element.dispatchEvent(new CustomEvent('shown.bs.modal', { bubbles: true }));
    }
    hide() {
      if (!this._element) return;
      this._element.classList.remove('show');
      this._element.style.display = 'none';
      this._element.setAttribute('aria-hidden', 'true');
      this._element.removeAttribute('aria-modal');
      document.body.classList.remove('modal-open');
      this._element.dispatchEvent(new CustomEvent('hidden.bs.modal', { bubbles: true }));
    }
    toggle() { this._element?.classList.contains('show') ? this.hide() : this.show(); }
    static getOrCreateInstance(element) { return new Modal(element); }
  }

  class Tab {
    constructor(element) { this._element = element; }
    show() {
      if (!this._element) return;
      const parent = this._element.closest('[role="tablist"],.nav');
      parent?.querySelectorAll('.active').forEach(node => node.classList.remove('active'));
      this._element.classList.add('active');
      const target = resolveTarget(this._element);
      if (target) {
        const host = target.parentElement;
        host?.querySelectorAll('.tab-pane.active,.tab-pane.show').forEach(node => node.classList.remove('active', 'show'));
        target.classList.add('active', 'show');
      }
      this._element.dispatchEvent(new CustomEvent('shown.bs.tab', { bubbles: true }));
    }
    static getOrCreateInstance(element) { return new Tab(element); }
  }

  window.bootstrap = Object.freeze({ Collapse, Dropdown, Modal, Tab });

  document.addEventListener('click', event => {
    const trigger = event.target.closest('[data-bs-toggle]');
    if (trigger) {
      const type = trigger.getAttribute('data-bs-toggle');
      if (type === 'collapse') {
        const target = resolveTarget(trigger);
        if (target) {
          event.preventDefault();
          target.classList.toggle('show');
          setExpanded(trigger, target.classList.contains('show'));
        }
      } else if (type === 'dropdown') {
        event.preventDefault();
        new Dropdown(trigger).toggle();
      } else if (type === 'modal') {
        const target = resolveTarget(trigger);
        if (target) {
          event.preventDefault();
          new Modal(target).show();
        }
      } else if (type === 'tab' || type === 'pill') {
        event.preventDefault();
        new Tab(trigger).show();
      }
    }

    const dismiss = event.target.closest('[data-bs-dismiss="modal"]');
    if (dismiss) {
      event.preventDefault();
      const modal = dismiss.closest('.modal');
      if (modal) new Modal(modal).hide();
    }

    if (!event.target.closest('.dropdown')) {
      document.querySelectorAll('.dropdown-menu.show').forEach(menu => menu.classList.remove('show'));
      document.querySelectorAll('[data-bs-toggle="dropdown"][aria-expanded="true"]').forEach(button => button.setAttribute('aria-expanded', 'false'));
    }
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    document.querySelectorAll('.modal.show').forEach(modal => new Modal(modal).hide());
    document.querySelectorAll('.dropdown-menu.show').forEach(menu => menu.classList.remove('show'));
  });
})();
